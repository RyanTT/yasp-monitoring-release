using YASP.Server.Application.Clustering;
using YASP.Server.Application.Monitoring;
using YASP.Server.Application.Monitoring.Objects;

namespace YASP.Server.Application.Persistence.Entries
{
    /// <summary>
    /// Raft log entry that assigns a monitor to a list of nodes with a given configuration active.
    /// </summary>
    public class ApplyMonitorNodeAssignmentEntry : EntryBase
    {
        public MonitorId MonitorId { get; set; }
        public List<NodeEndpoint> Nodes { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public MonitorConfigurationInstance MonitorConfiguration { get; set; }
    }
}
