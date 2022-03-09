using Cronos;

using Microsoft.Extensions.Logging;

using System.Net.Sockets;

using YASP.Server.Application.Clustering;
using YASP.Server.Application.Monitoring.Timeline;
using YASP.Server.Application.State;

namespace YASP.Server.Application.Monitoring
{
    /// <summary>
    /// Service that runs the checks for a monitor given a specific <see cref="MonitorCheckRunner.MonitorConfig"/>.
    /// </summary>
    public class MonitorCheckRunner
    {
        /// <summary>
        /// The config of the monitor this runner works with.
        /// </summary>
        public MonitorConfigurationInstance MonitorConfig { get; private set; }

        public CancellationTokenSource CancellationToken { get; private set; } = new CancellationTokenSource();

        private Task _task;
        private readonly ILogger<MonitorCheckRunner> _logger;
        private Node _node;
        private readonly IClusterService _clusterService;
        private readonly ApplicationStateService _applicationStateService;
        private readonly MonitorTimelineService _monitorTimelineService;

        public MonitorCheckRunner(
            ILogger<MonitorCheckRunner> logger,
            IClusterService clusterService,
            ApplicationStateService applicationStateService,
            MonitorTimelineService monitorTimelineService)
        {
            _logger = logger;
            _clusterService = clusterService;
            _applicationStateService = applicationStateService;
            _monitorTimelineService = monitorTimelineService;
        }

        /// <summary>
        /// Instructs the runner to start a thread that will perform the checks.
        /// </summary>
        /// <param name="monitor"></param>
        /// <returns></returns>
        public async Task StartAsync(MonitorConfigurationInstance monitor)
        {
            MonitorConfig = monitor;

            _node = await _clusterService.GetLocalNodeAsync(CancellationToken.Token);
            _task = Task.Run(() => RunAsync());
        }

        /// <summary>
        /// Main loop of the check thread.
        /// </summary>
        /// <returns></returns>
        private async Task RunAsync()
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Figure out when the next time is to run a check
                    var cronExpression = CronExpression.Parse(MonitorConfig.Value.Interval, CronFormat.IncludeSeconds);

                    var executionTime = cronExpression.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc, inclusive: true);

                    if (executionTime == null)
                    {
                        _logger.LogWarning($"No next check time found for monitor {MonitorConfig.Value.Id}. Stopping runner.");
                        return;
                    }

                    // Wait until then if the wait is > 0
                    var wait = executionTime.Value - DateTimeOffset.UtcNow;

                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, CancellationToken.Token);
                    }

                    CancellationToken.Token.ThrowIfCancellationRequested();

                    // Create a CancellationToken that will timeout either when this runner is supposed to stop working or when the timeout of the check is reached.
                    var timeoutToken = new CancellationTokenSource();
                    timeoutToken.CancelAfter(TimeSpan.FromSeconds(MonitorConfig.Value.Timeout));

                    var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.Token, timeoutToken.Token);

                    bool available = false;

                    //_logger.LogInformation($">> Check {executionTime.Value.ToString("HH:mm:ss")}: Running check");

                    // Run the check
                    if (MonitorConfig.Value.Http != null)
                    {
                        available = await RunHttpCheckAsync(combinedToken.Token);
                    }
                    else if (MonitorConfig.Value.Tcp != null)
                    {
                        available = await RunTcpCheckAsync(combinedToken.Token);
                    }

                    CancellationToken.Token.ThrowIfCancellationRequested();

                    //_logger.LogWarning($">> Check {executionTime.Value.ToString("HH:mm:ss")}: FAKE RESULT = {available}");

                    // Report the result of the check to the local timeline service
                    await _monitorTimelineService.ReportAsync(
                        MonitorConfig.Value.Id,
                        available ? Objects.MonitorStatusEnum.Reachable : Objects.MonitorStatusEnum.NotReachable,
                        executionTime.Value,
                        MonitorConfig);
                }
                catch (Exception ex)
                {

                }
            }
        }

        private async Task<bool> RunHttpCheckAsync(CancellationToken cancellationToken)
        {
            try
            {
                var client = new HttpClient();
                var request = new HttpRequestMessage();

                if (MonitorConfig.Value.Http.Method.ToLower() == "head")
                {
                    request.Method = HttpMethod.Head;
                }
                else
                {
                    request.Method = HttpMethod.Get;
                }

                request.RequestUri = new Uri(MonitorConfig.Value.Http.Url);

                var response = await client.SendAsync(request, cancellationToken);

                if (MonitorConfig.Value.Http.StatusCodes == null || MonitorConfig.Value.Http.StatusCodes.Length == 0)
                {
                    response.EnsureSuccessStatusCode();
                }
                else
                {
                    if (!MonitorConfig.Value.Http.IsValidStatusCode((int)response.StatusCode))
                    {
                        return false;
                    }
                }

                if (!string.IsNullOrEmpty(MonitorConfig.Value.Http.Keyword))
                {
                    var responseContentString = await response.Content.ReadAsStringAsync();

                    if (string.IsNullOrEmpty(responseContentString))
                    {
                        return false;
                    }

                    if (!responseContentString.ToLowerInvariant().Contains(MonitorConfig.Value.Http.Keyword.ToLowerInvariant()))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        private async Task<bool> RunTcpCheckAsync(CancellationToken cancellationToken)
        {
            using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                var connectTask = Task.Run(async () =>
                {
                    await socket.ConnectAsync(MonitorConfig.Value.Tcp.Host, MonitorConfig.Value.Tcp.Port, cancellationToken);

                    return true;
                });

                await connectTask.WaitAsync(cancellationToken);

                return connectTask.IsCompletedSuccessfully && connectTask.Result;
            }
            catch
            {
                return false;
            }
        }
    }
}
