
using YASP.Server.Application.Monitoring.Objects;

namespace YASP.Server.Application.Persistence.Objects
{
    /// <summary>
    /// Part of the application state. Marks that a notification was sent for a monitor at a check timestamp.
    /// </summary>
    public class NotificationSent
    {
        public MonitorId MonitorId { get; set; }
        public DateTimeOffset CheckTimestamp { get; set; }
        public string NotificationProviderIdentifier { get; set; }
    }
}
