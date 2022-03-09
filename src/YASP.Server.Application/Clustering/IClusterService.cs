using YASP.Server.Application.Clustering.Communication;
using YASP.Server.Application.Clustering.Discovery.Events;
using YASP.Server.Application.Persistence.Entries;
using YASP.Server.Application.Persistence.Events;

namespace YASP.Server.Application.Clustering
{
    /// <summary>
    /// Service that handles the interaction with the cluster aswell as the Raft log.
    /// </summary>
    public interface IClusterService
    {
        /// <summary>
        /// A node joined the cluster.
        /// </summary>
        event Action<MemberJoinedEventArgs> MemberJoined;

        /// <summary>
        /// A node left the cluster.
        /// </summary>
        event Action<MemberLeftEventArgs> MemberLeft;

        /// <summary>
        /// A node's availability changed (offline, online).
        /// </summary>
        event Action<MemberAvailabilityChangedEventArgs> MemberAvailabilityChanged;

        /// <summary>
        /// A new leader was elected or no leader is present.
        /// </summary>
        event Action<LeaderChangedEventArgs> LeaderChanged;

        /// <summary>
        /// An entry was added to the Raft log.
        /// </summary>
        event Action<MessageReceivedEventArgs> EntryAdded;

        /// <summary>
        /// Gets the list of last known cluster endpoints.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<IReadOnlyList<NodeEndpoint>> GetLastKnownClusterEndpointsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempts to add a node to the cluster. May only be called on the leader, otherwise will fail and return false.
        /// </summary>
        /// <param name="host">Node to add</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<bool> AddNodeAsync(NodeEndpoint host, CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempts to remove a node from the cluster. May only be called on the leader, other will fail and return false.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<bool> RemoveNodeAsync(NodeEndpoint host, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the list of nodes currently in the cluster.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<List<Node>> GetNodesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the node object representing the local node.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<Node> GetLocalNodeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets if the local node is the leader.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<bool> IsNodeLeaderAsync(CancellationToken token);

        /// <summary>
        /// Gets if the <paramref name="host"/> node is the leader.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<bool> IsNodeLeaderAsync(NodeEndpoint host, CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempts to write the <paramref name="entry"/> to the Raft log. May return false if replication wasn't possible and a retry is required.
        /// </summary>
        /// <typeparam name="TEntry"></typeparam>
        /// <param name="entry"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<bool> WriteAsync<TEntry>(TEntry entry, CancellationToken cancellationToken = default) where TEntry : EntryBase;

        /// <summary>
        /// Gets whether a leader is present and if so, also returns the leader.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<(bool hasLeader, NodeEndpoint leaderEndpoint)> TryGetLeaderAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a network <paramref name="request"/> to the <paramref name="nodeEndpoint"/>. May optionally be instructed to redirect the request to the leader.
        /// <para>Internally calls <see cref="INetworkConnector.SendAsync{TResponse}(NodeEndpoint, RequestBase{TResponse}, bool, CancellationToken)"/></para>.
        /// </summary>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="nodeEndpoint"></param>
        /// <param name="request"></param>
        /// <param name="redirectToLeader"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<TResponse> SendAsync<TResponse>(NodeEndpoint nodeEndpoint, RequestBase<TResponse> request, bool redirectToLeader = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a network <paramref name="request"/> to the leader. Will attempt to reach the leader through all known <see cref="GetLastKnownClusterEndpointsAsync(CancellationToken)"/>, otherwise throw a <see cref="InvalidOperationException"/>.
        /// <para>Internally calls <see cref="INetworkConnector.SendAsync{TResponse}(NodeEndpoint, RequestBase{TResponse}, bool, CancellationToken)"/> to all known available nodes.</para>
        /// </summary>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<TResponse> SendToLeaderAsync<TResponse>(RequestBase<TResponse> request, CancellationToken cancellationToken = default);
    }
}
