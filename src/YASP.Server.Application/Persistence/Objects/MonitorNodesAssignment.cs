
using YASP.Server.Application.Clustering;
using YASP.Server.Application.Monitoring;
using YASP.Server.Application.Monitoring.Objects;

namespace YASP.Server.Application.Persistence.Objects
{
    /// <summary>
    /// Part of the application state. Marks the assignment of a monitor to a group of nodes at a timestamp.
    /// </summary>
    [Serializable]
    public class MonitorNodesAssignment
    {
        public MonitorId MonitorId { get; set; }
        public List<NodeEndpoint> Nodes { get; set; }
        public MonitorConfigurationInstance MonitorConfiguration { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }
}
