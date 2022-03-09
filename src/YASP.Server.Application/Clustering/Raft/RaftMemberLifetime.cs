using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;

using MediatR;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using YASP.Server.Application.Options;

namespace YASP.Server.Application.Clustering.Raft
{
    /// <summary>
    /// Service to hookup to dotNext specific events and make them accessible for other components.
    /// </summary>
    public class RaftMemberLifetime : IClusterMemberLifetime
    {
        private readonly ILogger<RaftMemberLifetime> _logger;
        private readonly IMediator _mediator;
        private readonly IOptions<RootOptions> _options;
        private IRaftCluster _cluster;

        public event Action<IRaftCluster> OnStartEvent;
        public event Action<IRaftCluster> OnStopEvent;
        public event Action<IClusterMember> OnLeaderChangedEvent;
        public event Action<ClusterMemberEventArgs> OnMemberDiscovered;
        public event Action<ClusterMemberEventArgs> OnMemberGone;

        public RaftMemberLifetime(
            ILogger<RaftMemberLifetime> logger,
            IPersistentState persistentState,
            RaftClusterConfigurationStorage raftClusterMemory,
            IMediator mediator,
            IOptions<RootOptions> options)
        {
            _logger = logger;
            _mediator = mediator;
            _options = options;
        }

        public void OnStart(IRaftCluster cluster, IDictionary<string, string> metadata)
        {
            _cluster = cluster;
            cluster.PeerDiscovered += Cluster_PeerDiscovered;
            cluster.PeerGone += Cluster_PeerGone;
            cluster.LeaderChanged += Cluster_LeaderChanged;
            cluster.ReplicationCompleted += Cluster_ReplicationCompleted;

            OnStartEvent?.Invoke(cluster);
        }

        private void Cluster_ReplicationCompleted(DotNext.Net.Cluster.Replication.IReplicationCluster arg1, IClusterMember arg2)
        {

        }

        private void Cluster_LeaderChanged(DotNext.Net.Cluster.ICluster arg1, DotNext.Net.Cluster.IClusterMember arg2)
        {
            _ = Task.Run(() => OnLeaderChangedEvent?.Invoke(arg2));
        }

        private void Cluster_PeerGone(DotNext.Net.IPeerMesh arg1, DotNext.Net.PeerEventArgs arg2)
        {
            _ = Task.Run(() => OnMemberGone?.Invoke((ClusterMemberEventArgs)arg2));
        }

        private void Cluster_PeerDiscovered(DotNext.Net.IPeerMesh arg1, DotNext.Net.PeerEventArgs arg2)
        {
            _ = Task.Run(() => OnMemberDiscovered?.Invoke((ClusterMemberEventArgs)arg2));
        }

        public void OnStop(IRaftCluster cluster)
        {
            OnStopEvent?.Invoke(cluster);
        }
    }
}
