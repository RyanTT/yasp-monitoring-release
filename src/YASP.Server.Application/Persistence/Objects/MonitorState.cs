using YASP.Server.Application.Monitoring.Objects;

namespace YASP.Server.Application.Persistence.Objects
{
    /// <summary>
    /// Part of the application state. Marks the state of a monitor from a timestamp onwards.
    /// </summary>
    [Serializable]
    public class MonitorState
    {
        public MonitorId MonitorId { get; set; }

        public MonitorStatusEnum MonitorStatus { get; set; }

        public DateTimeOffset CheckTimestamp { get; set; }

        public bool IsRedundantEntry { get; set; }
    }
}
