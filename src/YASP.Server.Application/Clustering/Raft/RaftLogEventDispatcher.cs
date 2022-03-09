//using DotNext.Net.Cluster.Consensus.Raft;

//using System.Collections.Concurrent;

//namespace YASP.Server.Application.Clustering.Raft
//{
//    public class RaftLogEventDispatcher
//    {
//        private readonly IRaftCluster _raftCluster;
//        private readonly RaftMemoryBasedStateMachine _persistentState;
//        private ConcurrentQueue<IRaftLogEntry> _processableLogEntries = new ConcurrentQueue<IRaftLogEntry>();

//        public RaftLogEventDispatcher(IRaftCluster raftCluster, IPersistentState persistentState)
//        {
//            _raftCluster = raftCluster;
//            _persistentState = persistentState as RaftMemoryBasedStateMachine;

//            _raftCluster.ReplicationCompleted += _raftCluster_ReplicationCompleted;
//        }

//        private void _raftCluster_ReplicationCompleted(DotNext.Net.Cluster.Replication.IReplicationCluster arg1, DotNext.Net.Cluster.IClusterMember arg2)
//        {
//            Task.Run(async () => await _persistentState.ReplicationCompleted(_raftCluster.AuditTrail.LastCommittedEntryIndex));
//        }
//    }
//}
