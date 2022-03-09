using YASP.Server.Application.Monitoring.Objects;

namespace YASP.Server.Application.Persistence.Entries
{
    /// <summary>
    /// Raft log entry that represents a status change of a monitor to a new status.
    /// </summary>
    public class ApplyMonitorStateChangeEntry : EntryBase
    {
        public MonitorId MonitorId { get; set; }
        public MonitorStatusEnum Status { get; set; }
        public DateTimeOffset CheckTimestamp { get; set; }
        public bool IsRedundantEntry { get; set; }
    }
}
