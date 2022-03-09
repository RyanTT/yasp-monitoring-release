using System.Diagnostics.CodeAnalysis;

namespace YASP.Server.Application.Clustering
{
    /// <summary>
    /// Helper wrapper class around a <see cref="Uri"/>.
    /// </summary>
    public class NodeEndpoint : IEquatable<NodeEndpoint>
    {
        public string Host { get; set; }

        public static implicit operator NodeEndpoint(string host)
        {
            var uri = new Uri(host.ToLower());
            return new NodeEndpoint
            {
                Host = $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}"
            };
        }

        public static implicit operator string(NodeEndpoint nodeEndpoint)
        {
            return nodeEndpoint.Host;
        }

        public static implicit operator NodeEndpoint(Node node) => node.Endpoint;

        public static bool operator ==(NodeEndpoint n1, NodeEndpoint n2)
        {
            if (ReferenceEquals(n1, n2))
            {
                return true;
            }

            if (ReferenceEquals(n1, null))
            {
                return false;
            }

            if (ReferenceEquals(n2, null))
            {
                return false;
            }

            return n1.Equals(n2);
        }

        public static bool operator !=(NodeEndpoint n1, NodeEndpoint n2) => !(n1 == n2);

        public override bool Equals(object obj)
        {
            if (obj is NodeEndpoint nodeEndpoint) return nodeEndpoint.Equals(nodeEndpoint);

            return base.Equals(obj);
        }

        public bool Equals(NodeEndpoint other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            return Host.ToLower() == other.Host.ToLower();
        }

        public override string ToString()
        {
            return Host;
        }

        public class EqualityComparer : IEqualityComparer<NodeEndpoint>
        {
            public bool Equals(NodeEndpoint x, NodeEndpoint y)
            {
                if (x == null | y == null) return false;

                return x.Equals(y);
            }

            public int GetHashCode([DisallowNull] NodeEndpoint obj)
            {
                return obj.Host.GetHashCode();
            }
        }
    }
}
