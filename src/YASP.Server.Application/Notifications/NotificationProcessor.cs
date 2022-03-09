using Microsoft.Extensions.Logging;

using YASP.Server.Application.Clustering;
using YASP.Server.Application.Persistence.Entries;
using YASP.Server.Application.State;

namespace YASP.Server.Application.Notifications
{
    /// <summary>
    /// Service that runs on the leader and periodically checks whether a notification needs to be send out for a status change event.
    /// </summary>
    public class NotificationProcessor
    {
        private readonly ApplicationStateService _applicationStateService;
        private readonly IClusterService _clusterService;
        private readonly ILogger<NotificationProcessor> _logger;
        private readonly Dictionary<string, INotificationProvider> _notificationProviders;
        private CancellationTokenSource _cancellationTokenSource = new();
        private readonly Nito.AsyncEx.AsyncLock _lock = new Nito.AsyncEx.AsyncLock();
        private Task _task;

        public NotificationProcessor(
            ApplicationStateService applicationStateService,
            IEnumerable<INotificationProvider> notificationProviders,
            IClusterService clusterService,
            ILogger<NotificationProcessor> logger)
        {
            _applicationStateService = applicationStateService;
            _clusterService = clusterService;
            _logger = logger;
            _notificationProviders = notificationProviders.ToDictionary(x => x.Identifier, x => x);

            _clusterService.LeaderChanged += _clusterService_LeaderChanged;
        }

        /// <summary>
        /// Handles <see cref="IClusterService.LeaderChanged"/> and starts the loop sender thread if we are the leader.
        /// </summary>
        /// <param name="obj"></param>
        private async void _clusterService_LeaderChanged(Clustering.Discovery.Events.LeaderChangedEventArgs obj)
        {
            _ = Task.Run(async () =>
            {
                using var lk = await _lock.LockAsync();

                if (await _clusterService.IsNodeLeaderAsync(_cancellationTokenSource?.Token ?? default))
                {
                    // If we are the leader, cancel any old threads and start a new thread
                    _cancellationTokenSource?.Cancel();

                    if (_task != null && !_task.IsCompleted)
                    {
                        try
                        {
                            await _task.WaitAsync(TimeSpan.FromSeconds(30));
                        }
                        catch
                        {
                        }
                    }

                    _cancellationTokenSource = new CancellationTokenSource();
                    _task = RunAsync();
                }
                else
                {
                    _cancellationTokenSource?.Cancel();
                }
            });
        }

        /// <summary>
        /// Main loop that checks for notifications that need to be sent.
        /// </summary>
        /// <returns></returns>
        private async Task RunAsync()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));

                _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                // Get a snapshot of the application state
                var snapshot = await _applicationStateService.GetSnapshotAsync(_cancellationTokenSource.Token);

                // Are notifications enabled?
                if (!snapshot.AppConfiguration.Notifications.Enabled)
                {
                    continue;
                }

                // For each monitor..
                foreach (var state in snapshot.MonitorStates)
                {
                    // Ignore status changes that are marked as redundant (reduntant means its the same status as the status change before it)
                    if (state.IsRedundantEntry) continue;

                    foreach (var provider in _notificationProviders)
                    {
                        // Check if we haven't successfully issued a notification through this provider
                        bool hasSent = snapshot.NotificationsSent.Any(x =>
                            x.MonitorId == state.MonitorId &&
                            x.CheckTimestamp == state.CheckTimestamp &&
                            x.NotificationProviderIdentifier == provider.Key);

                        if (hasSent) continue;

                        try
                        {
                            bool success = await provider.Value.SendNotificationAsync(state, _cancellationTokenSource.Token);

                            if (!success)
                            {
                                _logger.LogWarning($"Provider {provider.GetType().Name} failed to send a notification for {state.MonitorId}!");
                                continue;
                            }

                            // Write into the log that this provider successfully sent a notification for this monitor ID at this check timestamp
                            await _clusterService.WriteAsync(new ApplyNotificationSentEntry
                            {
                                MonitorId = state.MonitorId,
                                CheckTimestamp = state.CheckTimestamp,
                                NotificationIdentifier = provider.Key
                            }, _cancellationTokenSource.Token);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Could not sent notification of status change for '{state.MonitorId}' to {state.MonitorStatus} via provider '{provider.Key}'!");
                        }
                    }
                }
            }
        }
    }
}
