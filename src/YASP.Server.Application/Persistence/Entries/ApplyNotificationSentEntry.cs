using YASP.Server.Application.Monitoring.Objects;

namespace YASP.Server.Application.Persistence.Entries
{
    /// <summary>
    /// Raft log entry that marks that a notification was sent.
    /// </summary>
    public class ApplyNotificationSentEntry : EntryBase
    {
        public MonitorId MonitorId { get; set; }
        public DateTimeOffset CheckTimestamp { get; set; }
        public string NotificationIdentifier { get; set; }
    }
}
