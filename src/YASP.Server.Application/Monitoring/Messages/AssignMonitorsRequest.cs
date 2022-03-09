using MediatR;

using YASP.Server.Application.Clustering.Communication;

namespace YASP.Server.Application.Monitoring.Messages
{
    /// <summary>
    /// Network request that assigns a list of monitors with configurations to a node.
    /// </summary>
    public class AssignMonitorsRequest : RequestBase<Unit>
    {
        /// <summary>
        /// List of monitors with their configurations.
        /// </summary>
        public List<MonitorConfigurationInstance> MonitorTaskInstances { get; set; }

        public class Handler : IRequestHandler<AssignMonitorsRequest>
        {
            private readonly MonitorTaskHandler _monitorTaskHandler;

            public Handler(MonitorTaskHandler monitorTaskHandler)
            {
                _monitorTaskHandler = monitorTaskHandler;
            }

            public async Task<Unit> Handle(AssignMonitorsRequest request, CancellationToken cancellationToken)
            {
                await _monitorTaskHandler.AssignMonitorTaskInstancesAsync(request.MonitorTaskInstances);

                return Unit.Value;
            }
        }
    }
}
