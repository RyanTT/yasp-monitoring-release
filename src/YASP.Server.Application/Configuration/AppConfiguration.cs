using YASP.Server.Application.Monitoring.Objects;
using YASP.Server.Application.Notifications;
using YASP.Server.Application.Pages;

namespace YASP.Server.Application.Configuration
{
    /// <summary>
    /// Application configuration.
    /// </summary>
    [Serializable]
    public class AppConfiguration
    {
        /// <summary>
        /// Revision.
        /// </summary>
        public int Revision { get; set; } = 0;

        /// <summary>
        /// Notifications configuration.
        /// </summary>
        public NotificationsConfiguration Notifications { get; set; } = new NotificationsConfiguration();

        /// <summary>
        /// List of status pages to be served by the nodes.
        /// </summary>
        public List<PageConfiguration> Pages { get; set; } = new List<PageConfiguration>();

        /// <summary>
        /// List of monitors to be monitored.
        /// </summary>
        public List<MonitorConfiguration> Monitors { get; set; } = new List<MonitorConfiguration>();
    }
}
