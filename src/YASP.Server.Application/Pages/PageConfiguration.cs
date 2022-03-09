namespace YASP.Server.Application.Pages
{
    [Serializable]
    public class PageConfiguration
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }

        public string Description { get; set; }

        public List<PageCategory> Categories { get; set; } = new List<PageCategory>();

        public class PageCategory
        {

            public string DisplayName { get; set; }

            public List<MonitorEntry> Monitors { get; set; } = new List<MonitorEntry>();

            public class MonitorEntry
            {
                public string MonitorId { get; set; }

                public string DisplayName { get; set; }
            }
        }
    }
}
