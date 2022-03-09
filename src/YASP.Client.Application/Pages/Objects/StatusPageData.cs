
using YASP.Shared.Objects;

namespace YASP.Client.Application.Pages.Objects
{
    public class StatusPageData
    {
        public PageDto ApiData { get; set; }
        public Dictionary<string, List<Segment>> Segments { get; set; } = new Dictionary<string, List<Segment>>();

        public class Segment
        {
            public string MonitorId { get; set; }
            public DateTimeOffset From { get; set; }
            public DateTimeOffset To { get; set; }
            public MonitorStatusEnumDto LowestStatus { get; set; }
            public MonitorStatusEnumDto InitialStatus { get; set; }
            public List<MonitorStateDto> StateChanges { get; set; } = new List<MonitorStateDto>();
        }
    }
}
