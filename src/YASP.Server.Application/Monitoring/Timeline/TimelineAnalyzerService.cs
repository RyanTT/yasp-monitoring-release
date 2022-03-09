
using Cronos;

using Microsoft.Extensions.Logging;

using YASP.Server.Application.Clustering;
using YASP.Server.Application.Monitoring.Messages;
using YASP.Server.Application.Monitoring.Objects;
using YASP.Server.Application.Persistence.Entries;
using YASP.Server.Application.Persistence.Objects;
using YASP.Server.Application.State;

namespace YASP.Server.Application.Monitoring.Timeline
{
    /// <summary>
    /// Service that uses the data of the local timelines of the nodes to determine when a monitor was online, 
    /// partially online or offline and writes that result into the Raft log.
    /// </summary>
    public class TimelineAnalyzerService
    {
        private readonly ILogger<TimelineAnalyzerService> _logger;
        private readonly ApplicationStateService _applicationStateService;
        private readonly IClusterService _clusterService;
        public Dictionary<MonitorId, Task> _analyzerTasks = new Dictionary<MonitorId, Task>(new MonitorId.EqualityComparer());

        private Nito.AsyncEx.AsyncLock _lock = new Nito.AsyncEx.AsyncLock();

        public TimelineAnalyzerService(ILogger<TimelineAnalyzerService> logger, ApplicationStateService applicationStateService, IClusterService clusterService)
        {
            _logger = logger;
            _applicationStateService = applicationStateService;
            _clusterService = clusterService;
        }

        /// <summary>
        /// Starts the analysis for a list of <paramref name="monitorIds"/> if no analysis is already running for that monitor.
        /// </summary>
        /// <param name="monitorIds"></param>
        /// <returns></returns>
        public async Task StartAnalysisForMonitorsIfNecessaryAsync(List<MonitorId> monitorIds)
        {
            using var lk = await _lock.LockAsync();

            foreach (var monitorId in monitorIds)
            {
                var task = _analyzerTasks.GetValueOrDefault(monitorId, default);

                if (task != null && !task.IsCompleted) continue; // Ignore request for monitors that already have a analyzer task running.

                // Run analysis
                task = Task.Run(() => AnalyzeTimelineAsync(monitorId));

                if (_analyzerTasks.ContainsKey(monitorId))
                {
                    _analyzerTasks.Remove(monitorId);
                }

                _analyzerTasks.Add(monitorId, task);
            }
        }

        /// <summary>
        /// Analyses a <paramref name="monitorId"/> from the last written <see cref="ApplyMonitorStateChangeEntry"/> to the present.
        /// </summary>
        /// <param name="monitorId"></param>
        /// <returns></returns>
        private async Task AnalyzeTimelineAsync(MonitorId monitorId)
        {
            try
            {
                _logger.LogDebug("Analyzing.");

                // Figure out to which check timestamp we already processed this monitor.
                var processedUntilTimestamp = _applicationStateService.ApplicationState.MonitorStates
                    .Where(x => x.MonitorId == monitorId)
                    .OrderByDescending(x => x.CheckTimestamp)
                    .Select(x => x.CheckTimestamp)
                    .FirstOrDefault();

                // Find all monitor assignments that were after the processedUntilTimestamp including the one that is valid for that specific time
                var assignmentsQuery = _applicationStateService.ApplicationState.MonitorNodesAssignments
                    .Where(x => x.MonitorId == monitorId);

                List<MonitorNodesAssignment> assignments = new List<MonitorNodesAssignment>();

                if (processedUntilTimestamp == default)
                {
                    assignments = assignmentsQuery.OrderBy(x => x.Timestamp).ToList();
                }
                else
                {
                    // Find the timestamp that was just before or at this processedUntilTimestamp
                    var activeAssignmentAtTimestamp = assignmentsQuery
                        .Where(x => x.Timestamp <= processedUntilTimestamp)
                        .OrderByDescending(x => x.Timestamp)
                        .FirstOrDefault();

                    if (activeAssignmentAtTimestamp == null)
                    {
                        _logger.LogWarning($"Attempted to analyze monitor {monitorId} but couldn't find a node assignment for timestamp {processedUntilTimestamp}");

                        activeAssignmentAtTimestamp = assignmentsQuery
                            .OrderByDescending(x => x.Timestamp)
                            .First();
                    }

                    assignments.Add(activeAssignmentAtTimestamp);

                    // Find all assignments that happened after the timestamp, so that we can figure out at which points
                    // to ask which nodes while analyzing the timeline
                    assignments.AddRange(assignmentsQuery.Where(x => x.Timestamp > processedUntilTimestamp).OrderBy(x => x.Timestamp).ToList());
                }

                // Given the assignments that were recorded, splice the analysis into parts equal in length to when the assignments were valid.
                List<(DateTimeOffset From, DateTimeOffset Until, MonitorNodesAssignment Assignment)> timeframes = new();

                for (int i = 0; i < assignments.Count; i++)
                {
                    var currentElement = assignments[i];
                    var nextElement = i + 1 < assignments.Count ? assignments[i + 1] : null;

                    if (nextElement == null)
                    {
                        timeframes.Add((currentElement.Timestamp, DateTimeOffset.MaxValue, currentElement));
                    }
                    else
                    {
                        timeframes.Add((currentElement.Timestamp, nextElement.Timestamp, currentElement));
                    }
                }

                // For each timeframe in which one assignment was valid..
                foreach (var timeframe in timeframes)
                {
                    // Request a timeline from each node
                    List<Task<NodeTimelineMapping>> timelineFetchTasks = GetNodeTimelines(timeframe.Assignment.Nodes, monitorId);

                    // Wait for all requests to either go through or fail
                    await Task.WhenAll(timelineFetchTasks);

                    var timelines = timelineFetchTasks
                        .Where(x => x.IsCompletedSuccessfully)
                        .Where(x => x.Result != null)
                        .Select(x => x.Result)
                        .ToList();

                    DateTimeOffset checkTimestamp = timeframe.From;

                    // While our iterating checkTimestamp is inside the timeframe and not in the future..
                    while (checkTimestamp < timeframe.Until && checkTimestamp <= DateTime.UtcNow)
                    {
                        // If the checkTimestamp is smaller than the timestamp of the last written status change entry, then skip forward to that timestamp.
                        // This may happen if the first assignement we iterate through was written before the last written status change entry.
                        if (checkTimestamp < processedUntilTimestamp)
                        {
                            checkTimestamp = processedUntilTimestamp.AddTicks(1);
                        }

                        // Find the next timestamp where a check should have been performed.
                        var cronExpression = CronExpression.Parse(timeframe.Assignment.MonitorConfiguration.Value.Interval, CronFormat.IncludeSeconds);
                        var nextTimestamp = cronExpression.GetNextOccurrence(checkTimestamp, TimeZoneInfo.Utc, inclusive: true);

                        if (!nextTimestamp.HasValue)
                        {
                            // Jump to next timeframe because this timeframe doesn't have a check time left
                            break;
                        }

                        // Exit if the next timestamp would be outside of the range of our current timeframe
                        if (nextTimestamp >= timeframe.Until) break;

                        checkTimestamp = nextTimestamp.Value;

                        // Check if any timeline has an event for this checkTimestamp.
                        // If not, continue to the next timestamp;
                        if (!timelines.Any(tl => tl.Timeline.Any(e => e.FromCheckTimestamp == checkTimestamp)))
                        {
                            checkTimestamp = checkTimestamp.AddTicks(1);

                            continue;
                        }

                        // Figure out how long we should wait after the checkTimestamp for results from the nodes.
                        // This is the max time a all nodes should take to finish their checks plus some buffer time.
                        var waitUntil = checkTimestamp.AddSeconds(timeframe.Assignment.MonitorConfiguration.Value.Timeout).AddSeconds(10);

                        CancellationTokenSource timeout = new CancellationTokenSource();

                        // Calculate how long to wait depending on what time it is now. This may be negative if the checkTimestamp plus waitUntil is in the past.
                        var timeoutTimespan = waitUntil - DateTimeOffset.UtcNow;

                        // If we have a negative timeout, it means we shouldn't have to wait for a timeout at all (the event is in the past),
                        // because the affected event is in the past and we can assume any node we reach already has it's check result written into his local timeline for this timestamp
                        // or definitively has no data.
                        if (timeoutTimespan > TimeSpan.Zero)
                        {
                            timeout.CancelAfter(timeoutTimespan);

                            List<Task> updateTimelineTasks = new();

                            // Update timelines from nodes if we don't have sufficient data
                            foreach (var nodeEndpoint in timeframe.Assignment.Nodes)
                            {
                                // If we got here, atleast one node has an event for this checkTimestamp.
                                // Make sure we wait with a max timeout of checkTimestamp + timeout + networkTime for results of all nodes that should provide one!
                                var data = timelines.FirstOrDefault(x => x.NodeEndpoint == nodeEndpoint);

                                // Check if we already have data for our target checkTimestamp, if so, we don't need to request new data
                                if (data != null && data.Timeline.Any(x => x.FromCheckTimestamp <= checkTimestamp && checkTimestamp <= x.ToCheckTimestamp))
                                {
                                    continue;
                                }

                                var t = Task.Run(async () =>
                                {
                                    while (!timeout.IsCancellationRequested)
                                    {
                                        try
                                        {
                                            var response = await _clusterService.SendAsync(nodeEndpoint, new GetTimelineRequest
                                            {
                                                MonitorId = monitorId
                                            });

                                            timeout.Token.ThrowIfCancellationRequested();

                                            if (response.Timeline.Any(x => x.FromCheckTimestamp <= checkTimestamp && checkTimestamp <= x.ToCheckTimestamp))
                                            {
                                                // Update the reference to the refreshed timeline
                                                timelines.First(x => x.NodeEndpoint == nodeEndpoint).Timeline = response.Timeline;

                                                // We can stop early since this node successfully provided data!
                                                break;
                                            }
                                            else
                                            {
                                                _logger.LogDebug($">> Check {checkTimestamp.ToString("HH:mm:ss")}: Waiting for timeline that has a usable event entry from {nodeEndpoint}.");

                                                await Task.Delay(TimeSpan.FromSeconds(1));
                                            }
                                        }
                                        catch
                                        {
                                        }
                                    }
                                });

                                updateTimelineTasks.Add(t);
                            }

                            await Task.WhenAll(updateTimelineTasks);
                        }

                        // Count of votes
                        int isOnlineVotes = 0;
                        int isOfflineVotes = 0;

                        // We don't count anyone who doesn't have data for this event
                        foreach (var timeline in timelines)
                        {
                            // Find the entry that represents this timelines view of the checkTimestamp.
                            // If we don't find an entry, it means this node doesnt know the status at this checkTimestamp and this timeline will not count into the voting.
                            var entry = timeline.Timeline.FirstOrDefault(x => x.FromCheckTimestamp <= checkTimestamp && checkTimestamp <= x.ToCheckTimestamp);

                            if (entry == null) continue;
                            if (entry.MonitorStatus == MonitorStatusEnum.NotReachable) isOfflineVotes++;
                            if (entry.MonitorStatus == MonitorStatusEnum.Reachable) isOnlineVotes++;
                        }

                        if (isOnlineVotes + isOfflineVotes == 0)
                        {
                            // No votes, no way of telling whats going on. Iterate to next checkTimestamp.
                            checkTimestamp = checkTimestamp.AddTicks(1);
                            continue;
                        }


                        _logger.LogDebug($">> Check {checkTimestamp.ToString("HH:mm:ss")}: Vote result was online={isOnlineVotes}, offline={isOfflineVotes}");

                        var totalVotes = isOnlineVotes + isOfflineVotes;

                        // How many nodes saw the monitor online?
                        double onlinePercentage = (double)isOnlineVotes / totalVotes;

                        // Figure out what the last status was we wrote into the Raft log.
                        var lastStatus = _applicationStateService.ApplicationState.MonitorStates.ToList()
                                .Where(x => x.MonitorId == monitorId)
                                .OrderBy(x => x.CheckTimestamp)
                                .LastOrDefault();

                        // Evaluate what new status to write into the log.

                        if (onlinePercentage >= timeframe.Assignment.MonitorConfiguration.Value.AvailableThreshold)
                        {
                            _logger.LogInformation($"{monitorId} is online!");

                            await _clusterService.WriteAsync(new ApplyMonitorStateChangeEntry
                            {
                                CheckTimestamp = checkTimestamp,
                                MonitorId = monitorId,
                                Status = MonitorStatusEnum.Reachable,
                                IsRedundantEntry = lastStatus != null && lastStatus.MonitorStatus == MonitorStatusEnum.Reachable
                            });
                        }
                        else if (onlinePercentage >= timeframe.Assignment.MonitorConfiguration.Value.PartialThreshold)
                        {
                            _logger.LogInformation($"{monitorId} is partially online!");

                            await _clusterService.WriteAsync(new ApplyMonitorStateChangeEntry
                            {
                                CheckTimestamp = checkTimestamp,
                                MonitorId = monitorId,
                                Status = MonitorStatusEnum.PartiallyReachable,
                                IsRedundantEntry = lastStatus != null && lastStatus.MonitorStatus == MonitorStatusEnum.PartiallyReachable
                            });
                        }
                        else
                        {
                            _logger.LogInformation($"{monitorId} is offline!");

                            await _clusterService.WriteAsync(new ApplyMonitorStateChangeEntry
                            {
                                CheckTimestamp = checkTimestamp,
                                MonitorId = monitorId,
                                Status = MonitorStatusEnum.NotReachable,
                                IsRedundantEntry = lastStatus != null && lastStatus.MonitorStatus == MonitorStatusEnum.NotReachable
                            });
                        }

                        checkTimestamp = checkTimestamp.AddTicks(1);
                    }
                }

                _logger.LogDebug("Analyzing ended.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception occurred while attempting to analyze the timeline for monitor {monitorId}!");
            }
        }

        /// <summary>
        /// Helper method that starts a new thread for each request to a node to get their timeline data.
        /// </summary>
        /// <param name="nodes"></param>
        /// <param name="monitorId"></param>
        /// <returns></returns>
        private List<Task<NodeTimelineMapping>> GetNodeTimelines(IEnumerable<NodeEndpoint> nodes, MonitorId monitorId)
        {
            List<Task<NodeTimelineMapping>> timelineFetchTasks = new();

            // Request a timeline from each node
            foreach (var nodeEndpoint in nodes)
            {
                // Ask each responsible node for his timeline of this monitor
                var t = Task.Run(async () =>
                {
                    try
                    {
                        var response = await _clusterService.SendAsync(nodeEndpoint, new GetTimelineRequest
                        {
                            MonitorId = monitorId
                        });

                        return new NodeTimelineMapping
                        {
                            NodeEndpoint = nodeEndpoint,
                            Timeline = response.Timeline
                        };
                    }
                    catch
                    {
                    }

                    return null;
                });

                timelineFetchTasks.Add(t);
            }

            return timelineFetchTasks;
        }

        /// <summary>
        /// Helper class to associate a timeline to a node.
        /// </summary>
        private class NodeTimelineMapping
        {
            public NodeEndpoint NodeEndpoint { get; set; }
            public List<MonitorTimelineService.TimelineSegment> Timeline { get; set; }
        }
    }
}
