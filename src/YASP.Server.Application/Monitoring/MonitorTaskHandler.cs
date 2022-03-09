using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;

using YASP.Server.Application.State;

namespace YASP.Server.Application.Monitoring
{
    /// <summary>
    /// Service that receives a list of assigned monitors and makes sure that for each assigned monitor a <see cref="MonitorCheckRunner"/> exists.
    /// </summary>
    public class MonitorTaskHandler
    {
        private readonly ILogger<MonitorTaskHandler> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ApplicationStateService _applicationStateService;
        private Task _handlerTask = default;
        private CancellationTokenSource _handlerTaskToken = default;
        private ConcurrentBag<MonitorConfigurationInstance> _monitorTaskInstances = new ConcurrentBag<MonitorConfigurationInstance>();
        private List<MonitorCheckRunner> _runners = new List<MonitorCheckRunner>();
        private Nito.AsyncEx.AsyncLock _asyncLock = new Nito.AsyncEx.AsyncLock();

        public MonitorTaskHandler(ILogger<MonitorTaskHandler> logger, IServiceProvider serviceProvider, ILoggerFactory loggerFactory, ApplicationStateService applicationStateService)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _loggerFactory = loggerFactory;
            _applicationStateService = applicationStateService;
        }

        /// <summary>
        /// Gets the monitors that we are currently running runners for.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task<IReadOnlyList<MonitorConfigurationInstance>> GetMonitorTaskInstancesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<MonitorConfigurationInstance>)_monitorTaskInstances.ToList());
        }

        public Task AssignMonitorTaskInstancesAsync(IReadOnlyList<MonitorConfigurationInstance> monitorTaskInstances)
        {
            if (monitorTaskInstances.Any())
            {
                _logger.LogInformation($"Node was assigned the monitoring of: {string.Join(", ", monitorTaskInstances.Select(x => x.Value.Id))}.");
            }
            else
            {
                _logger.LogInformation($"Node was assigned the monitoring of no monitors. Idling.");
            }

            _handlerTaskToken?.Cancel();
            _handlerTaskToken = new CancellationTokenSource();
            _handlerTask = Task.Run(() => RunAsync(monitorTaskInstances.ToList()));

            return Task.CompletedTask;
        }

        /// <summary>
        /// Makes sure that outdated runners are cancelled and new ones are created where necessary.
        /// </summary>
        /// <param name="monitors"></param>
        /// <returns></returns>
        private async Task RunAsync(List<MonitorConfigurationInstance> monitors)
        {
            // This method should never run concurrently
            using var asyncLock = await _asyncLock.LockAsync(_handlerTaskToken.Token);

            // Find all runners that are not running with an up-to-date configuration or that are no longer assigned to us
            foreach (var runner in _runners.Where(x => !monitors.Any(newConfig => newConfig.Hash == x.MonitorConfig.Hash)).ToList())
            {
                // Shut down runner
                runner.CancellationToken.Cancel();

                _logger.LogInformation($"Cancelling check runner for monitor {runner.MonitorConfig.Value.Id}");

                _runners.Remove(runner);
            }

            // Find all monitors that we aren't running a runner for yet
            var toBeStartedMonitors = monitors.Where(monitor => !_runners.Any(runner => runner.MonitorConfig.Hash == monitor.Hash)).ToList();

            foreach (var monitor in toBeStartedMonitors)
            {
                _logger.LogInformation($"Starting check runner for monitor {monitor.Value.Id}.");

                // Get a new runner instance from the dependency container. Since this is a transient service, a new one will be created each time.
                var runner = _serviceProvider.GetService<MonitorCheckRunner>();

                await runner.StartAsync(monitor);

                _runners.Add(runner);
            }
        }
    }
}
