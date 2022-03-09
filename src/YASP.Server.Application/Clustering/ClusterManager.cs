using DotNext.Net;
using DotNext.Net.Cluster.Consensus.Raft;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using YASP.Server.Application.Clustering.Communication;
using YASP.Server.Application.Clustering.Communication.Http;
using YASP.Server.Application.Clustering.Discovery.Messages;
using YASP.Server.Application.Clustering.Raft;
using YASP.Server.Application.Configuration;
using YASP.Server.Application.Notifications;
using YASP.Server.Application.Options;
using YASP.Server.Application.State;

namespace YASP.Server.Application.Clustering
{
    /// <summary>
    /// Service that automatically attempts to join a cluster via the given discovery endpoints.
    /// </summary>
    public class ClusterManager : IHostedService
    {
        private readonly ClusterOptions _options;
        private readonly IClusterService _clusterService;
        private readonly ILogger<ClusterManager> _logger;
        private readonly IRaftCluster _raftCluster;
        private readonly IPeerMesh<IRaftClusterMember> _peerMesh;
        private readonly INetworkConnector _networkConnector;
        private readonly NotificationProcessor _notificationsHandler;
        private CancellationTokenSource _cancellationTokenSource;

        protected Task<List<Node>> Nodes => _clusterService.GetNodesAsync();

        public ClusterManager(
            IOptions<RootOptions> options,
            IClusterService clusterService,
            ILogger<ClusterManager> logger,
            IRaftCluster raftCluster,
            IPeerMesh<IRaftClusterMember> peerMesh,
            INetworkConnector networkConnector,
            IPersistentState persistentState,
            NotificationProcessor notificationsHandler)
        {
            _options = options.Value.Cluster;
            _clusterService = clusterService;
            _logger = logger;
            _raftCluster = raftCluster;
            _peerMesh = peerMesh;
            _networkConnector = networkConnector;
            _notificationsHandler = notificationsHandler;
        }

        // Dependency container configuration
        public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<ClusterManager>().AddSingleton<IHostedService>(provider => provider.GetRequiredService<ClusterManager>());
            services.AddSingleton<IClusterService, RaftService>();
            services.AddSingleton<INetworkConnector, HttpNetworkConnector>();
            services.AddSingleton<AppConfigurationFileHandler>().AddSingleton<IHostedService>(provider => provider.GetRequiredService<AppConfigurationFileHandler>());
            services.AddSingleton<ApplicationStateService>();


            return RaftService.ConfigureServices(services, configuration);
        }

        // Host builder configuration
        public static IHostBuilder ConfigureHostBuilder(IHostBuilder builder)
        {
            return RaftService.ConfigureHostBuilder(builder);
        }

        // Pipeline configuration
        public static IApplicationBuilder ConfigureApplicationBuilder(IApplicationBuilder app)
        {
            return RaftService.ConfigureApplicationBuilder(app);
        }

        public static IEndpointRouteBuilder ConfigureEndpointRouteBuilder(IEndpointRouteBuilder endpoints)
        {
            HttpNetworkConnector.ConfigureEndpointRouteBuilder(endpoints);

            return endpoints;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource = new CancellationTokenSource();

            _logger.LogInformation($"Starting cluster management");

            if (!_options.DiscoveryEndpoints.Any())
            {
                _logger.LogInformation($"No discovery endpoints found. Starting as single cluster.");
            }
            else
            {
                _ = Task.Run(async () => await TryDiscoveryEndpointsAsync());
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource.Cancel();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Main loop to handle attempting to join a cluster via the provided discovery endpoints.
        /// </summary>
        /// <returns></returns>
        private async Task TryDiscoveryEndpointsAsync()
        {
            var discoveryEndpoints = _options.DiscoveryEndpoints.Select(x => (NodeEndpoint)x).ToList();
            var lastKnownNodes = await _clusterService.GetLastKnownClusterEndpointsAsync();

            // We test all our discovery endpoints first, and then we add all nodes we already knew from a previous run
            discoveryEndpoints.AddRange(lastKnownNodes);

            discoveryEndpoints = discoveryEndpoints.Distinct().ToList();

            _logger.LogInformation($"Attempting to join cluster via {_options.DiscoveryEndpoints.Length} endpoints defined as discovery or previously known.");

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                foreach (var discoveryEndpoint in _options.DiscoveryEndpoints)
                {
                    // If we have a leader, we are already in a succesful cluster.
                    if ((await _clusterService.TryGetLeaderAsync(_cancellationTokenSource.Token)).hasLeader)
                    {
                        _logger.LogInformation($"Joined cluster.");
                        return;
                    }

                    _logger.LogInformation($"Trying discovery endpoint {discoveryEndpoint}");

                    try
                    {
                        // Send the join network request to the node.
                        var response = await _clusterService.SendAsync(discoveryEndpoint, new JoinClusterRequest
                        {
                            Endpoint = _options.ListenEndpoint
                        }, redirectToLeader: true);

                        _logger.LogInformation($"Joined cluster.");
                        return;
                    }
                    catch
                    {
                        _logger.LogDebug($"Could not reach endpoint.");
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }
}
