using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;

using YASP.Server.Application.Clustering;
using YASP.Server.Application.Monitoring.Objects;
using YASP.Server.Application.Monitoring.Timeline.Messages;
using YASP.Server.Application.Persistence.Entries;
using YASP.Server.Application.State;

namespace YASP.Server.Application.Monitoring.Timeline
{
    /// <summary>
    /// Service that keeps track of the data that this node recorded when checking for availability of its assigned monitors.
    /// </summary>
    public class MonitorTimelineService
    {
        private Nito.AsyncEx.AsyncLock _lock = new Nito.AsyncEx.AsyncLock();
        private ConcurrentDictionary<MonitorId, TimelineState> _timelines = new(new MonitorId.EqualityComparer());
        private readonly IClusterService _clusterService;
        private readonly ApplicationStateService _applicationStateService;
        private readonly ILogger<MonitorTimelineService> _logger;
        private readonly Task _sendTask;
        private readonly Nito.AsyncEx.AsyncAutoResetEvent _doSendEvent = new Nito.AsyncEx.AsyncAutoResetEvent();

        public MonitorTimelineService(IClusterService clusterService, ApplicationStateService applicationStateService, ILogger<MonitorTimelineService> logger)
        {
            _clusterService = clusterService;
            _applicationStateService = applicationStateService;
            _logger = logger;
            _applicationStateService.AppStateUpdated += _applicationStateService_AppStateUpdated;

            _sendTask = Task.Run(SendLoopAsync);
        }

        // Cleanup of local timelines. We can safely dispose entries of our timelines that are
        // before the timestamp of the latest monitor state update.
        private async void _applicationStateService_AppStateUpdated(EntryBase obj)
        {
            using var lk = await _lock.LockAsync();
            var state = await _applicationStateService.GetSnapshotAsync();

            foreach (var monitorId in state.MonitorStates.Select(x => x.MonitorId))
            {
                if (!_timelines.ContainsKey(monitorId)) return;

                var processedUntil = state.MonitorStates
                    .Where(x => x.MonitorId == monitorId)
                    .OrderByDescending(x => x.CheckTimestamp)
                    .FirstOrDefault()?.CheckTimestamp;

                // Clear the timeline and only leave events beyond the processedUntil timestamp AND always leave the last event too
                var timeline = _timelines[monitorId].Timeline;

                var toRemove = timeline.SkipLast(1).Where(x => x.ToCheckTimestamp == null || x.ToCheckTimestamp <= processedUntil).ToList();
                timeline.RemoveAll(x => toRemove.Contains(x));
            }
        }

        private class TimelineState
        {
            public MonitorStatusEnum LastStatus { get; set; } = MonitorStatusEnum.Unknown;
            public List<TimelineSegment> Timeline { get; set; } = new List<TimelineSegment>();
        }

        /// <summary>
        /// Send loop that will figure out which monitors have timeline data that the leader should analyze.
        /// </summary>
        /// <returns></returns>
        private async Task SendLoopAsync()
        {
            // TODO: Add a cancellationtoken here
            while (true)
            {
                await _doSendEvent.WaitAsync();

                // Figure out all monitor ids that have a timeline event stored for them
                List<MonitorId> monitorsThatHaveEvents = new();

                using (var lk = await _lock.LockAsync())
                {
                    foreach (var entry in _timelines)
                    {
                        if (entry.Value.Timeline.Count > 1)
                        {
                            monitorsThatHaveEvents.Add(entry.Key);
                        }
                    }
                }

                while (true)
                {
                    try
                    {
                        // Notify our leader that we have events on our timelines that need processing
                        await _clusterService.SendToLeaderAsync(new TimelinesUpdatedRequest
                        {
                            MonitorIds = monitorsThatHaveEvents
                        });

                        break;
                    }
                    catch
                    {

                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }

        /// <summary>
        /// Called by the <see cref="MonitorCheckRunner"/> whenever a check was performed. Will gradually record all check results in a local timeline which will be transmitted to the leader as soon as possible.
        /// </summary>
        /// <param name="monitorId"></param>
        /// <param name="status"></param>
        /// <param name="checkTimestamp"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        public async Task ReportAsync(MonitorId monitorId, MonitorStatusEnum status, DateTimeOffset checkTimestamp, MonitorConfigurationInstance config)
        {
            using var lk = await _lock.LockAsync();

            CreateTimelineIfNecessary(monitorId);

            _timelines.TryGetValue(monitorId, out var about);

            // Check if the last known status is equal to this one, and if so, just update its timestamp
            if (about.LastStatus == status)
            {
                about.Timeline.Last().ToCheckTimestamp = checkTimestamp;

                return;
            }

            // If we got here, the last status does not equal the new status, so we must begin a new timeline segment

            // Make sure the last timeline segment ends just before our new one
            about.Timeline.Last().ToCheckTimestamp = checkTimestamp.AddTicks(-1);

            // Create the new timeline segment with our gathered data
            about.Timeline.Add(new TimelineSegment
            {
                MonitorId = monitorId,
                FromCheckTimestamp = checkTimestamp,
                ToCheckTimestamp = checkTimestamp,
                MonitorStatus = status
            });

            var previousStatus = about.LastStatus;
            about.LastStatus = status;

            // Make sure the sender thread sends out our notification
            _doSendEvent.Set();

            _logger.LogWarning($"Monitor {monitorId} changed status {previousStatus} -> {about.LastStatus}!");
        }

        /// <summary>
        /// Gets the local timeline of a <paramref name="monitorId"/>.
        /// </summary>
        /// <param name="monitorId"></param>
        /// <returns></returns>
        public async Task<List<TimelineSegment>> GetAsync(MonitorId monitorId)
        {
            using var lk = await _lock.LockAsync();

            CreateTimelineIfNecessary(monitorId);

            _timelines.TryGetValue(monitorId, out var timeline);

            // Create copy with ToList()
            return timeline.Timeline.ToList();
        }

        private void CreateTimelineIfNecessary(MonitorId monitorId)
        {
            if (_timelines.ContainsKey(monitorId)) return;

            _timelines.TryAdd(monitorId, new TimelineState
            {
                LastStatus = MonitorStatusEnum.Unknown,
                Timeline = new List<TimelineSegment> {
                    new TimelineSegment {
                        MonitorId = monitorId,
                        MonitorStatus = MonitorStatusEnum.Unknown
                    }
                }
            });
        }

        /// <summary>
        /// This class represents a timeframe on the timeline of a monitor and what status the local node saw for the monitor during that time.
        /// </summary>
        [Serializable]
        public class TimelineSegment
        {
            public MonitorId MonitorId { get; set; }
            public MonitorStatusEnum MonitorStatus { get; set; }
            public DateTimeOffset? FromCheckTimestamp { get; set; }
            public DateTimeOffset? ToCheckTimestamp { get; set; }
        }
    }
}
