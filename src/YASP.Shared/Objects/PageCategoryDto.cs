namespace YASP.Shared.Objects
{
    public class PageCategoryDto
    {
        public string DisplayName { get; set; }

        public List<PageCategoryMonitorDto> Monitors { get; set; } = new List<PageCategoryMonitorDto>();
    }
}
