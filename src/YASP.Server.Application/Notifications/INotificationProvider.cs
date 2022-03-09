using YASP.Server.Application.Monitoring.Objects;
using YASP.Server.Application.Persistence.Objects;

namespace YASP.Server.Application.Notifications
{
    /// <summary>
    /// Interface for notification providers.
    /// </summary>
    public interface INotificationProvider
    {
        /// <summary>
        /// Unique identifier of the provider that is used to determine whether a notification for this provider was already sent.
        /// </summary>
        string Identifier { get; }

        /// <summary>
        /// Sends a notification for this <paramref name="state"/>
        /// </summary>
        /// <param name="state"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<bool> SendNotificationAsync(MonitorState state, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets whether this provider would send a notification for this <paramref name="monitorId"/>.
        /// </summary>
        /// <param name="monitorId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<bool> HandlesMonitorAsync(MonitorId monitorId, CancellationToken cancellationToken = default);
    }
}
