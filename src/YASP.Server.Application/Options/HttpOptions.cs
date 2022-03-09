using Microsoft.Extensions.Configuration;

namespace YASP.Server.Application.Options
{
    public class HttpOptions
    {
        public ProxyOptions Proxy { get; set; } = new ProxyOptions();

        [ConfigurationKeyName("require_ssl")]
        public bool RequireSsl { get; set; }
    }
}
