using System.Diagnostics.CodeAnalysis;

namespace YASP.Server.Application.Monitoring.Objects
{
    /// <summary>
    /// Helper class that represents the ID of a monitor.
    /// </summary>
    public class MonitorId : IEquatable<MonitorId>
    {
        public string Identifier { get; set; }

        public static implicit operator MonitorId(string identifier)
        {
            return new MonitorId
            {
                Identifier = identifier.ToLowerInvariant()
            };
        }

        public static implicit operator string(MonitorId monitorId) => monitorId.Identifier;

        public static bool operator ==(MonitorId n1, MonitorId n2)
        {
            return n1.Equals(n2);
        }

        public static bool operator !=(MonitorId n1, MonitorId n2) => !(n1 == n2);

        public override bool Equals(object obj)
        {
            if (obj is MonitorId monitorId) return Equals(monitorId);

            return base.Equals(obj);
        }

        public bool Equals(MonitorId other)
        {
            if (ReferenceEquals(other, null)) return false;
            return Identifier.ToLower() == other.Identifier.ToLower();
        }

        public override string ToString()
        {
            return Identifier;
        }

        public class EqualityComparer : IEqualityComparer<MonitorId>
        {
            public bool Equals(MonitorId x, MonitorId y)
            {
                if (x == null || y == null) return false;

                return x.Equals(y);
            }

            public int GetHashCode([DisallowNull] MonitorId obj)
            {
                return obj.Identifier.GetHashCode();
            }
        }
    }
}
