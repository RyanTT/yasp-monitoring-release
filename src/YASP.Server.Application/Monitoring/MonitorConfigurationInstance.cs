using YASP.Server.Application.Monitoring.Objects;

namespace YASP.Server.Application.Monitoring
{
    /// <summary>
    /// Wrapper for a <see cref="MonitorConfiguration"/> that also holds the <see cref="MonitorConfigurationInstance.Hash"/> of it.
    /// </summary>
    [Serializable]
    public class MonitorConfigurationInstance : IEquatable<MonitorConfigurationInstance>
    {
        public string Hash { get; set; }
        public MonitorConfiguration Value { get; set; }

        public bool Equals(MonitorConfigurationInstance other) => Hash.ToLowerInvariant() == other.Hash.ToLowerInvariant();
    }
}
