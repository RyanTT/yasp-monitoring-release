using DotNext.IO.Log;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Http;
using DotNext.Net.Cluster.Consensus.Raft.Membership;
using DotNext.Net.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Nito.AsyncEx;

using YASP.Server.Application.Clustering.Communication;
using YASP.Server.Application.Clustering.Communication.Http;
using YASP.Server.Application.Clustering.Discovery.Events;
using YASP.Server.Application.Clustering.Raft.StateMachine;
using YASP.Server.Application.Options;
using YASP.Server.Application.Persistence.Entries;
using YASP.Server.Application.Persistence.Events;

namespace YASP.Server.Application.Clustering.Raft
{
    /// <summary>
    /// Implementation of <see cref="IClusterService"/> that utilizes the dotNext library.
    /// </summary>
    public class RaftService : IClusterService
    {
        private const string DOTNEXT_RAFT_URL = "/api/cluster/raft";
        private readonly IOptions<RootOptions> _options;
        private readonly IRaftHttpCluster _raftCluster;
        private readonly ILogger<RaftService> _logger;
        private readonly RaftClusterConfigurationStorage _raftClusterMemory;
        private readonly INetworkConnector _networkConnector;
        private readonly MemoryStateMachine _persistentState;
        private readonly RaftMemberLifetime _clusterEvents;
        private readonly AsyncLock _membershipLock = new();

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public event Action<MemberJoinedEventArgs> MemberJoined;

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public event Action<MemberLeftEventArgs> MemberLeft;

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public event Action<MemberAvailabilityChangedEventArgs> MemberAvailabilityChanged;

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public event Action<MessageReceivedEventArgs> EntryAdded;

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public event Action<LeaderChangedEventArgs> LeaderChanged;

        /// <summary>
        /// List of nodes currently known to the cluster.
        /// </summary>
        public List<Node> Nodes { get; private set; } = new List<Node>();

        /// <summary>
        /// Configures the application services and adds dotNext specific services.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration) => services
            .AddSingleton<IClusterConfigurationStorage<HttpEndPoint>, RaftClusterConfigurationStorage.Bridge>()
            .AddSingleton<IClusterMemberLifetime, RaftMemberLifetime>()
            .AddSingleton<RaftClusterConfigurationStorage>()
            .AddSingleton<MemoryStateMachine>()
            .AddSingleton<IPersistentState>(x => x.GetService<MemoryStateMachine>())
            .AddSingleton<IAuditTrail<IRaftLogEntry>>(x => x.GetService<MemoryStateMachine>())
            .AddSingleton<IHttpMessageHandlerFactory, RaftClientHandlerFactory>();

        /// <summary>
        /// dotNext specific required calls. Configures the dotNext library with things such as heartbeat timeout, election timeout.
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static IHostBuilder ConfigureHostBuilder(IHostBuilder builder)
        {
            builder.JoinCluster((memberConfiguration, configuration, host) =>
            {
                var heartbeatThreshold = configuration.GetValue("DOTNEXT_HEARTBEAT_THRESHOLD", 0.5);
                var lowerElectionTimeout = configuration.GetValue("DOTNEXT_LOWER_TIMEOUT", 30000);
                var upperElectionTimeout = configuration.GetValue("DOTNEXT_UPPER_TIMEOUT", lowerElectionTimeout * 2);

                memberConfiguration.PublicEndPoint = new HttpEndPoint(new Uri(configuration.GetValue<string>("cluster:listen_on")));
                memberConfiguration.ColdStart = false;

                memberConfiguration.HeartbeatThreshold = heartbeatThreshold;
                memberConfiguration.LowerElectionTimeout = lowerElectionTimeout;
                memberConfiguration.UpperElectionTimeout = upperElectionTimeout;

                memberConfiguration.ProtocolPath = DOTNEXT_RAFT_URL;
            });

            return builder;
        }

        /// <summary>
        /// dotNext specific setup calls. Also makes sure that the <see cref="HttpNetworkConnector.ENDPOINT_ENSURE_LEADER"/> is redirected to the leader node.
        /// </summary>
        /// <param name="app"></param>
        /// <returns></returns>
        public static IApplicationBuilder ConfigureApplicationBuilder(IApplicationBuilder app)
        {
            return app
                .Use(async (context, next) =>
                {
                    if (context.Request.Path.StartsWithSegments(DOTNEXT_RAFT_URL))
                    {
                        if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value != 0)
                        {
                            context.Request.ContentLength = null;
                        }
                    }

                    await next(context);
                })
                .UseConsensusProtocolHandler()
                .RedirectToLeader(HttpNetworkConnector.ENDPOINT_ENSURE_LEADER);
        }

        public RaftService(
            IOptions<RootOptions> options,
            IRaftHttpCluster raftCluster,
            ILogger<RaftService> logger,
            RaftClusterConfigurationStorage raftClusterMemory,
            INetworkConnector networkConnector,
            IClusterMemberLifetime clusterEvents,
            IPersistentState persistentState)
        {
            _options = options;
            _raftCluster = raftCluster;
            _logger = logger;
            _raftClusterMemory = raftClusterMemory;
            _networkConnector = networkConnector;
            _persistentState = persistentState as MemoryStateMachine;
            _clusterEvents = clusterEvents as RaftMemberLifetime;

            _clusterEvents.OnStopEvent += _clusterEvents_OnStopEvent;
            _clusterEvents.OnMemberDiscovered += _clusterEvents_OnMemberDiscovered;
            _clusterEvents.OnMemberGone += _clusterEvents_OnMemberGone;
            _clusterEvents.OnLeaderChangedEvent += _clusterEvents_OnLeaderChangedEvent;
            _persistentState.EntryAdded += _persistentState_EntryAdded;
        }

        /// <summary>
        /// Mirror the <see cref="RaftMemoryBasedStateMachine.EntryAdded"/> event to <see cref="IClusterService.EntryAdded"/>.
        /// </summary>
        /// <param name="obj"></param>
        private void _persistentState_EntryAdded(Persistence.Entries.EntryBase obj)
        {
            EntryAdded?.Invoke(new MessageReceivedEventArgs
            {
                Entry = obj
            });

            _logger.LogDebug($"Entry committed: {obj.GetType().Name}");
        }

        /// <summary>
        /// Handles dotNext leader changed events and reflects that in our custom list of <see cref="Nodes"/>.
        /// <para>Also mirrors the event to <see cref="IClusterService.LeaderChanged"/></para>.
        /// </summary>
        /// <param name="obj"></param>
        private void _clusterEvents_OnLeaderChangedEvent(IClusterMember obj)
        {
            var nodeEndpoint = obj != null ? ((HttpEndPoint)obj.EndPoint).AsNodeEndpoint() : null;

            if (nodeEndpoint == null)
            {
                _logger.LogInformation($"No leader.");

                foreach (var node in Nodes)
                {
                    node.IsLeader = false;
                }

                LeaderChanged?.Invoke(new LeaderChangedEventArgs { Node = null });
            }
            else
            {
                _logger.LogInformation($"Leader is: {nodeEndpoint}");

                foreach (var node in Nodes)
                {
                    if (node.Endpoint == nodeEndpoint)
                    {
                        node.IsLeader = true;

                        LeaderChanged?.Invoke(new LeaderChangedEventArgs { Node = node });

                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Handles dotNext member gone events (node left the cluster) to maintain our custom list of <see cref="Nodes"/>.
        /// <para>Also mirrors the event to <see cref="IClusterService.MemberLeft"/></para>.
        /// </summary>
        /// <param name="obj"></param>
        private void _clusterEvents_OnMemberGone(ClusterMemberEventArgs obj)
        {
            var nodeEndpoint = ((HttpEndPoint)obj.PeerAddress).AsNodeEndpoint();

            _logger.LogInformation($"Node left the cluster: {nodeEndpoint}");

            var node = Nodes.FirstOrDefault(x => x.Endpoint == nodeEndpoint);

            if (node != null) Nodes.Remove(node);

            MemberLeft?.Invoke(new MemberLeftEventArgs
            {
                Node = node
            });

            _ = Task.Run(() => _raftClusterMemory.WriteLastKnownEndpointsAsync(Nodes));
        }

        /// <summary>
        /// Handles dotNext member discovered events (node joined the cluster) to maintain our custom list of <see cref="Nodes"/>.
        /// <para>Also mirrors the event to <see cref="IClusterService.MemberJoined"/></para>.
        /// </summary>
        /// <param name="obj"></param>
        private async void _clusterEvents_OnMemberDiscovered(ClusterMemberEventArgs obj)
        {
            var nodeEndpoint = ((HttpEndPoint)obj.PeerAddress).AsNodeEndpoint();

            _logger.LogInformation($"Node joined the cluster: {nodeEndpoint}");

            obj.Member.MemberStatusChanged += Member_MemberStatusChanged;

            var newNode = new Node
            {
                IsLeader = false,
                Endpoint = nodeEndpoint,
                Status = MapAvailabilityStatus(obj.Member.Status)
            };

            Nodes.Add(newNode);

            MemberJoined?.Invoke(new MemberJoinedEventArgs
            {
                Node = newNode
            });

            _ = Task.Run(() => _raftClusterMemory.WriteLastKnownEndpointsAsync(Nodes));
        }

        /// <summary>
        /// Handles dotNext member status change events (node went from reachable to unreachable for example) to maintain our custom list of <see cref="Nodes"/>.
        /// <para>Also mirrors the event to <see cref="IClusterService.MemberAvailabilityChanged"/>.</para>
        /// </summary>
        /// <param name="obj"></param>
        private void Member_MemberStatusChanged(ClusterMemberStatusChangedEventArgs obj)
        {
            var node = Nodes.FirstOrDefault(x => x.Endpoint == ((HttpEndPoint)obj.PeerAddress).AsNodeEndpoint());

            // Ignore updates to member status that are the same status (dotNext bug?)
            if (node.Status == MapAvailabilityStatus(obj.Member.Status)) return;

            _logger.LogInformation($"Node {((HttpEndPoint)obj.PeerAddress).AsNodeEndpoint()} changed to availability status {obj.Member.Status}");


            if (node == null) return;

            node.Status = MapAvailabilityStatus(obj.Member.Status);

            MemberAvailabilityChanged?.Invoke(new MemberAvailabilityChangedEventArgs
            {
                Node = node
            });
        }

        private NodeAvailabilityStatus MapAvailabilityStatus(ClusterMemberStatus status) => status switch
        {
            ClusterMemberStatus.Unknown => NodeAvailabilityStatus.Unknown,
            ClusterMemberStatus.Unavailable => NodeAvailabilityStatus.Unavailable,
            ClusterMemberStatus.Available => NodeAvailabilityStatus.Available,
            _ => throw new NotImplementedException(),
        };

        private void _clusterEvents_OnStopEvent(IRaftCluster obj)
        {

        }

        /// <inheritdoc/>
        public async Task<bool> AddNodeAsync(NodeEndpoint host, CancellationToken cancellationToken = default)
        {
            var cancelToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token).Token;

            using var lk = await _membershipLock.LockAsync(cancelToken);

            var endpoint = new HttpEndPoint(new Uri(host));
            var clusterMemberId = ClusterMemberId.FromEndPoint(endpoint);

            return await _raftCluster.AddMemberAsync(clusterMemberId, endpoint, cancelToken);
        }

        /// <inheritdoc/>
        public async Task<bool> RemoveNodeAsync(NodeEndpoint host, CancellationToken cancellationToken = default)
        {
            using var lk = await _membershipLock.LockAsync(cancellationToken);

            var endpoint = new HttpEndPoint(new Uri(host));

            try
            {
                bool success = await _raftCluster.RemoveMemberAsync(ClusterMemberId.FromEndPoint(endpoint), cancellationToken);

                _logger.LogInformation($"Removed node from cluster {endpoint}");

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to remove node from cluster {endpoint}");

                return false;
            }
        }

        /// <inheritdoc/>
        public Task<List<Node>> GetNodesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Nodes);
        }

        /// <inheritdoc/>
        public Task<bool> IsNodeLeaderAsync(CancellationToken token)
        {
            return IsNodeLeaderAsync(null, token);
        }

        /// <inheritdoc/>
        public async Task<bool> IsNodeLeaderAsync(NodeEndpoint host, CancellationToken cancellationToken = default)
        {
            if (host == null)
            {
                host = _raftCluster.LocalMemberAddress.AsNodeEndpoint();
            }

            var (hasLeader, leaderEndpoint) = await TryGetLeaderAsync(cancellationToken);

            if (!hasLeader) return false;

            return leaderEndpoint == host;
        }

        /// <inheritdoc/>
        public Task<(bool hasLeader, NodeEndpoint leaderEndpoint)> TryGetLeaderAsync(CancellationToken cancellationToken = default)
        {
            var leaderMember = ((IRaftCluster)_raftCluster).Members.FirstOrDefault(x => x.IsLeader);

            if (leaderMember == null) return Task.FromResult<(bool, NodeEndpoint)>((false, null));

            return Task.FromResult((true, ((HttpEndPoint)leaderMember.EndPoint).AsNodeEndpoint()));
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<NodeEndpoint>> GetLastKnownClusterEndpointsAsync(CancellationToken cancellationToken = default)
        {
            return await _raftClusterMemory.GetLastKnownEndpointsAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public Task<TResponse> SendAsync<TResponse>(NodeEndpoint nodeEndpoint, RequestBase<TResponse> request, bool redirectToLeader = false, CancellationToken cancellationToken = default)
        {
            return _networkConnector.SendAsync(nodeEndpoint, request, redirectToLeader, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<TResponse> SendToLeaderAsync<TResponse>(RequestBase<TResponse> request, CancellationToken cancellationToken = default)
        {
            var members = await GetNodesAsync(cancellationToken);

            //members.Remove(_raftCluster.LocalMemberAddress.AsNodeEndpoint());

            List<(bool, NodeEndpoint)> endpointsOrderedByIsLeader = new();

            foreach (var member in members)
            {
                endpointsOrderedByIsLeader.Add((await IsNodeLeaderAsync(member, cancellationToken), member));
            }

            // OrderByDescending will return the leader first and then all other nodes
            // Try sending it to all nodes with redirectToLeader: true and see if we can reach him this way. If not, error out.
            foreach (var endpoint in endpointsOrderedByIsLeader.OrderByDescending(x => x.Item1).Select(x => x.Item2))
            {
                try
                {
                    return await _networkConnector.SendAsync(endpoint, request, redirectToLeader: true, cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    //_logger.LogError(ex, $"Could not send leader request to {endpoint}");
                }
            }

            throw new InvalidOperationException("Leader not reachable.");
        }

        /// <inheritdoc/>
        public Task<bool> WriteAsync<TEntry>(TEntry entry, CancellationToken cancellationToken = default) where TEntry : EntryBase
        {
            _logger.LogDebug($"RaftService::WriteAsync: Creating log entry");
            var logEntry = new RaftJsonLogEntry(entry, _persistentState.Term);

            _logger.LogDebug($"RaftService::WriteAsync: Replicating");
            return _raftCluster.ReplicateAsync(logEntry, cancellationToken);

            //var a = _persistentState.CreateJsonLogEntry(entry);
            //return _raftCluster.ReplicateAsync(a, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<Node> GetLocalNodeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Nodes.First(x => x.Endpoint == _options.Value.Cluster.ListenEndpoint));
        }
    }
}
