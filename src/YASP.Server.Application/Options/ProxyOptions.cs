using Microsoft.Extensions.Configuration;

namespace YASP.Server.Application.Options
{
    public class ProxyOptions
    {
        [ConfigurationKeyName("trust_all")]
        public bool TrustAll { get; set; }
    }
}