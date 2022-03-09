
using Microsoft.Extensions.Logging;

using Nito.AsyncEx;

using YASP.Server.Application.Clustering;
using YASP.Server.Application.Persistence.Entries;

namespace YASP.Server.Application.State
{
    /// <summary>
    /// Service that listens for committed entries of the Raft log and constructs an application state.
    /// </summary>
    public class ApplicationStateService
    {
        private readonly ILogger<ApplicationStateService> _logger;
        private readonly IClusterService _clusterService;

        /// <summary>
        /// Event raised whenever the app configuration of the application state was updated.
        /// </summary>
        public event Action AppConfigurationUpdated;

        /// <summary>
        /// Event raised whenever the application state was updated.
        /// </summary>
        public event Action<EntryBase> AppStateUpdated;

        /// <summary>
        /// The current application state object. May not be safe to read from. Use <see cref="GetSnapshotAsync(CancellationToken)"/> to receive a safe copy.
        /// </summary>
        public ApplicationStateSnapshot ApplicationState { get; private set; }

        private AsyncLock _stateLock = new AsyncLock();

        public ApplicationStateService(ILogger<ApplicationStateService> logger, IClusterService clusterService)
        {
            _logger = logger;
            _clusterService = clusterService;
            _clusterService.EntryAdded += ClusterService_MessageReceivedAsync;

            ApplicationState = new ApplicationStateSnapshot();
        }

        /// <summary>
        /// Gets a snapshot of the current application build that is safe to read from.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ApplicationStateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            using (var lk = await _stateLock.LockAsync())
            {
                return ApplicationState.CreateCopy();
            }
        }

        /// <summary>
        /// Handles <see cref="IClusterService.EntryAdded"/>. Applies the <see cref="EntryBase"/> to the application state.
        /// </summary>
        /// <param name="obj"></param>
        private async void ClusterService_MessageReceivedAsync(Persistence.Events.MessageReceivedEventArgs obj)
        {
            using (var lk = await _stateLock.LockAsync())
            {
                ApplicationState.Apply(obj.Entry);

                switch (obj.Entry)
                {
                    case ApplicationStateSnapshot entry:
                        {
                            AppConfigurationUpdated?.Invoke();

                            break;
                        }

                    case ApplyConfigurationEntry entry:
                        {
                            AppConfigurationUpdated?.Invoke();

                            break;
                        }
                }
            }

            AppStateUpdated?.Invoke(obj.Entry);
        }
    }
}
