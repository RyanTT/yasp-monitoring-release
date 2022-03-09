namespace YASP.Shared.Objects
{
    public class PageCategoryMonitorDto
    {
        public string MonitorId { get; set; }

        public string DisplayName { get; set; }

        public List<MonitorStateDto> States { get; set; } = new List<MonitorStateDto>();
    }
}
