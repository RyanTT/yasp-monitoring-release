using System.Diagnostics.CodeAnalysis;

namespace YASP.Server.Application.Clustering
{
    /// <summary>
    /// Simple object that represents a node of the cluster.
    /// </summary>
    public class Node : IEquatable<Node>
    {
        /// <summary>
        /// Endpoint of the node.
        /// </summary>
        public NodeEndpoint Endpoint { get; set; }

        /// <summary>
        /// Indicates whether this node is a leader.
        /// </summary>
        public bool IsLeader { get; set; }

        /// <summary>
        /// Availability status of the node.
        /// </summary>
        public NodeAvailabilityStatus Status { get; set; }

        public static implicit operator string(Node node) => node.Endpoint;

        public bool Equals(Node other)
        {
            return Endpoint == other.Endpoint;
        }

        public class EqualityComparer : IEqualityComparer<Node>
        {
            public bool Equals(Node x, Node y)
            {
                return x.Equals(y);
            }

            public int GetHashCode([DisallowNull] Node obj)
            {
                return obj.Endpoint.Host.GetHashCode();
            }
        }
    }
}
