using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;
using System.Text.Json;

using YASP.Server.Application.Clustering;
using YASP.Server.Application.Clustering.Communication;
using YASP.Server.Application.Monitoring.Messages;
using YASP.Server.Application.Persistence.Entries;
using YASP.Server.Application.State;
using YASP.Server.Application.Utilities;

namespace YASP.Server.Application.Monitoring
{
    /// <summary>
    /// Service that runs on the leader and manages the distribution/assignment of monitors to the nodes.
    /// </summary>
    public class MonitorTaskDistributor : IHostedService
    {
        private readonly ILogger<MonitorTaskDistributor> _logger;
        private readonly IClusterService _clusterService;
        private readonly ApplicationStateService _applicationStateService;
        private readonly INetworkConnector _networkConnector;
        private CancellationTokenSource _schedulerCancelToken;
        private Task _task;
        private Debouncer _configChangedDebouncer = new Debouncer();
        private ConcurrentDictionary<Node, List<MonitorConfigurationInstance>> _distribution = new ConcurrentDictionary<Node, List<MonitorConfigurationInstance>>(new Node.EqualityComparer());

        public MonitorTaskDistributor(
            ILogger<MonitorTaskDistributor> logger,
            IClusterService clusterService,
            ApplicationStateService applicationStateService,
            INetworkConnector networkConnector)
        {
            _logger = logger;
            _clusterService = clusterService;
            _applicationStateService = applicationStateService;
            _networkConnector = networkConnector;
            _clusterService.LeaderChanged += _clusterService_LeaderChanged;
            _clusterService.MemberAvailabilityChanged += ClusterService_MemberAvailabilityChangedAsync;
            _applicationStateService.AppConfigurationUpdated += ApplicationStateService_AppConfigurationUpdatedAsync;
        }

        /// <summary>
        /// Handles <see cref="ApplicationStateService.AppConfigurationUpdated"/>. Starts redistribution if this node is the leader.
        /// </summary>
        private async void ApplicationStateService_AppConfigurationUpdatedAsync()
        {
            if (!await _clusterService.IsNodeLeaderAsync(null)) return;

            _configChangedDebouncer.Throttle(TimeSpan.FromSeconds(3), async () =>
            {
                _logger.LogWarning($"Application configuration changed, redistributing monitors where necessary.");

                _ = StartOrRestartDistributionAsync();
            });
        }

        /// <summary>
        /// Handles <see cref="IClusterService.MemberAvailabilityChanged"/>. Starts redistribution if this node is the leader.
        /// </summary>
        /// <param name="obj"></param>
        private async void ClusterService_MemberAvailabilityChangedAsync(Clustering.Discovery.Events.MemberAvailabilityChangedEventArgs obj)
        {
            if (!await _clusterService.IsNodeLeaderAsync(null)) return;

            _configChangedDebouncer.Throttle(TimeSpan.FromSeconds(3), async () =>
            {
                _logger.LogWarning($"Node {obj.Node.Endpoint} is now {obj.Node.Status}, redistributing monitors where necessary.");

                _ = StartOrRestartDistributionAsync();
            });
        }

        /// <summary>
        /// Handles <see cref="IClusterService.LeaderChanged"/>. Starts redistribution if this node is the leader.
        /// </summary>
        /// <param name="obj"></param>
        private async void _clusterService_LeaderChanged(Clustering.Discovery.Events.LeaderChangedEventArgs obj)
        {
            if (!await _clusterService.IsNodeLeaderAsync(null))
            {
                _schedulerCancelToken?.Cancel();

                return;
            }

            _logger.LogInformation($"Local node became leader, starting distribution of monitors.");

            _ = StartOrRestartDistributionAsync();
        }

        /// <summary>
        /// Starts a new distribution of monitors. If a distribution is already running, it will be cancelled and a new one will start after the old one was successfully cancelled.
        /// </summary>
        /// <returns></returns>
        private async Task StartOrRestartDistributionAsync()
        {
            // Cancel any existing scheduler task
            _schedulerCancelToken?.Cancel();
            _schedulerCancelToken = new CancellationTokenSource();

            if (_task != null)
            {
                await _task.WaitAsync(_schedulerCancelToken.Token);
            }

            // Run distribution
            _task = Task.Run(DistributeAsync);
        }

        /// <summary>
        /// (Re)distribute monitors to the nodes.
        /// </summary>
        /// <returns></returns>
        private async Task DistributeAsync()
        {
            while (!_schedulerCancelToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug("MonitorTaskDistributor::DistributeAsync: 1");

                    // Get all nodes that are available
                    var nodes = (await _clusterService.GetNodesAsync()).Where(x => x.Status == NodeAvailabilityStatus.Available).ToList();

                    // Get all monitors with their config hashes
                    var monitors = _applicationStateService.ApplicationState.AppConfiguration.Monitors.Select(x => new MonitorConfigurationInstance
                    {
                        Hash = SHA1Hash.Hash(JsonSerializer.Serialize(x)),
                        Value = x
                    });

                    _logger.LogDebug("MonitorTaskDistributor::DistributeAsync: 2");

                    // Get the assigned monitors from each node if don't have that list cached locally yet.
                    if (_distribution == null || _distribution.Count == 0)
                    {
                        List<Task> fetchTasks = new();

                        foreach (var node in nodes)
                        {
                            fetchTasks.Add(Task.Run(async () =>
                            {
                                var response = await _networkConnector.SendAsync(node, new GetMonitorTaskInstancesRequest(), cancellationToken: _schedulerCancelToken.Token);

                                _distribution.AddOrUpdate(node, response.Instances, (node, list) => response.Instances);
                            }));
                        }

                        await Task.WhenAll(fetchTasks);
                    }

                    _logger.LogDebug("MonitorTaskDistributor::DistributeAsync: 3");


                    foreach (var node in nodes)
                    {
                        if (!_distribution.ContainsKey(node))
                        {
                            _distribution.TryAdd(node, new List<MonitorConfigurationInstance>());
                        }
                    }

                    _schedulerCancelToken.Token.ThrowIfCancellationRequested();

                    // Find all nodes we distributed something to but which are not in the cluster or available anymore and remove them from our distribution
                    _distribution.Keys.Where(node => !nodes.Any(x => x == node && x.Status == NodeAvailabilityStatus.Available)).ToList().ForEach(removedNode =>
                    {
                        _distribution.Remove(removedNode, out var assignedMonitors);
                    });

                    _logger.LogDebug("MonitorTaskDistributor::DistributeAsync: 4");

                    // For each node, remove any assigned monitors that are not wanted anymore (removed from configuration)
                    foreach (var kv in _distribution.ToList())
                    {
                        var node = kv.Key;
                        var assignedMonitors = kv.Value;

                        assignedMonitors.RemoveAll(monitor => !monitors.Any(x => x.Hash == monitor.Hash));
                    }

                    _schedulerCancelToken.Token.ThrowIfCancellationRequested();

                    _logger.LogDebug("MonitorTaskDistributor::DistributeAsync: 5");

                    // For each monitor config, find all suitable nodes and remove those that *had* been assigned the monitor, but that are now not qualifying anymore
                    foreach (var monitor in monitors)
                    {
                        var suitableNodes = FindSuitableNodesForMonitorConfiguration(monitor, nodes);

                        foreach (var kv in _distribution)
                        {
                            var node = kv.Key;
                            var assignedMonitors = kv.Value;

                            if (!suitableNodes.Contains(node) && assignedMonitors.Contains(monitor))
                            {
                                // Remove from assigned monitor list
                                assignedMonitors.Remove(monitor);
                            }
                        }
                    }

                    _schedulerCancelToken.Token.ThrowIfCancellationRequested();

                    List<Node> FindSuitableNodesForMonitorConfiguration(MonitorConfigurationInstance version, IReadOnlyList<Node> nodes)
                    {
                        // For now we don't have any logic here, but in the future we might implement some sort of feature
                        // to assign monitors only to specific nodes!
                        return nodes.ToList();
                    }

                    _logger.LogDebug("MonitorTaskDistributor::DistributeAsync: 6");

                    // For each monitor configuration, check how many nodes should check the target and if necessary, add more to the distribution
                    foreach (var monitor in monitors)
                    {
                        _schedulerCancelToken.Token.ThrowIfCancellationRequested();

                        // Figure out if we are checking the monitor from too few nodes
                        var runsOnAmount = _distribution.Count(x => x.Value.Contains(monitor));

                        if (runsOnAmount < monitor.Value.CheckWith)
                        {
                            // Find all nodes we haven't assigned the monitor to
                            var remainingNodes = _distribution.Where(x => !x.Value.Contains(monitor)).Select(x => x.Key).ToList();

                            // Filter for all nodes that are suitable for this monitor
                            remainingNodes = FindSuitableNodesForMonitorConfiguration(monitor, remainingNodes);

                            // Sort all remaining nodes by their amount of assigned monitors so that we can assign to those first,
                            // who have the least monitors assigned already
                            remainingNodes = remainingNodes.OrderBy(node =>
                            {
                                if (!_distribution.ContainsKey(node)) return 0;

                                return _distribution[node].Count;
                            }).ToList();

                            // Run the following loop so many times until CheckWith is actually fulfilled
                            for (int i = 0; i < (monitor.Value.CheckWith - runsOnAmount); i++)
                            {
                                _schedulerCancelToken.Token.ThrowIfCancellationRequested();

                                if (remainingNodes.Count == 0)
                                {
                                    _logger.LogWarning($"Configured monitor {monitor.Value.Id} is supposed to be checked with " +
                                        $"{monitor.Value.CheckWith} nodes, however only {_distribution.Count(x => x.Value.Contains(monitor))} are available.");

                                    break;
                                }
                                else
                                {
                                    var node = remainingNodes.First();

                                    remainingNodes.Remove(node);

                                    // Add that this node handles this monitor now too
                                    _distribution.AddOrUpdate(node, new List<MonitorConfigurationInstance> { monitor }, (node, entries) => entries.Append(monitor).ToList());
                                }
                            }
                        }
                    }

                    _logger.LogDebug("MonitorTaskDistributor::DistributeAsync: 7");

                    _schedulerCancelToken.Token.ThrowIfCancellationRequested();

                    DateTimeOffset assignmentTime = DateTimeOffset.UtcNow;

                    // Write each assignment into the log
                    foreach (var monitorId in _distribution.SelectMany(x => x.Value.Select(x => x.Value.Id)))
                    {
                        var assignedNodes = _distribution.Where(x => x.Value.Any(x => x.Value.Id == monitorId)).Select(x => x.Key).ToList();

                        _logger.LogDebug($"MonitorTaskDistributor::DistributeAsync: Calling WriteAsync for monitor {monitorId}");

                        await _clusterService.WriteAsync(new ApplyMonitorNodeAssignmentEntry
                        {
                            MonitorId = monitorId,
                            Nodes = assignedNodes.Select(x => x.Endpoint).ToList(),
                            Timestamp = assignmentTime,
                            MonitorConfiguration = monitors.First(x => x.Value.Id == monitorId)
                        }, _schedulerCancelToken.Token);
                    }

                    _logger.LogDebug("MonitorTaskDistributor::DistributeAsync: 8");

                    // Send each node his assigned monitors
                    foreach (var kv in _distribution)
                    {
                        _schedulerCancelToken.Token.ThrowIfCancellationRequested();

                        var node = kv.Key;
                        var assignedMonitors = kv.Value;

                        _logger.LogDebug($"Assigning {node.Endpoint} {assignedMonitors.Count} monitoring tasks.");

                        while (!_schedulerCancelToken.IsCancellationRequested)
                        {
                            try
                            {
                                await _networkConnector.SendAsync(node, new AssignMonitorsRequest
                                {
                                    MonitorTaskInstances = assignedMonitors
                                });

                                break; // Stop send-rety loop
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Could not transmit assigned monitors to {node.Endpoint}, retrying..");
                                await Task.Delay(1000);
                            }
                        }
                    }


                    _logger.LogInformation($"Monitor distribution completed.");

                    // End retry loop
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to distribute monitors. Reattempting in 10 seconds. ({ex.Message})");

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10)).WaitAsync(_schedulerCancelToken.Token);
                    }
                    catch
                    {
                        // If we got here, our task was asked to cancel its run.
                        break;
                    }
                }
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
