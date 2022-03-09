using Microsoft.Extensions.Configuration;

namespace YASP.Server.Application.Options
{
    public class RootOptions
    {
        [ConfigurationKeyName("http")]
        public HttpOptions Http { get; set; }

        [ConfigurationKeyName("cluster")]
        public ClusterOptions Cluster { get; set; }
    }
}
