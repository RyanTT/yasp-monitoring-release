using Microsoft.Extensions.Configuration;

namespace YASP.Server.Application.Options
{
    public class ClusterOptions
    {
        [ConfigurationKeyName("api_key")]
        public string ApiKey { get; set; }

        [ConfigurationKeyName("listen_on")]
        public string ListenEndpoint { get; set; }

        [ConfigurationKeyName("discovery_endpoints")]
        public string[] DiscoveryEndpoints { get; set; } = Array.Empty<string>();
    }
}
