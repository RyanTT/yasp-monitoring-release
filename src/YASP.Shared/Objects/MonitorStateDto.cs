namespace YASP.Shared.Objects
{
    public class MonitorStateDto
    {
        public string MonitorId { get; set; }

        public MonitorStatusEnumDto MonitorStatus { get; set; }

        public DateTimeOffset CheckTimestamp { get; set; }

        public bool IsRedundantEntry { get; set; }
    }
}
