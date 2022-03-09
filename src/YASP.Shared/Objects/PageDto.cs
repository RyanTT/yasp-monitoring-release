namespace YASP.Shared.Objects
{
    public class PageDto
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }

        public string Description { get; set; }

        public List<PageCategoryDto> Categories { get; set; } = new List<PageCategoryDto>();
    }
}
