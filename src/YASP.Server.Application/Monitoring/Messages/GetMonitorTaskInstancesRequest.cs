using MediatR;

using YASP.Server.Application.Clustering.Communication;

namespace YASP.Server.Application.Monitoring.Messages
{
    /// <summary>
    /// Network request that fetches the currently monitored monitors and their configurations from a node.
    /// </summary>
    public class GetMonitorTaskInstancesRequest : RequestBase<GetMonitorTaskInstancesRequest.Response>
    {
        public class Handler : IRequestHandler<GetMonitorTaskInstancesRequest, Response>
        {
            private readonly MonitorTaskHandler _monitorTaskHandler;

            public Handler(MonitorTaskHandler monitorTaskHandler)
            {
                _monitorTaskHandler = monitorTaskHandler;
            }

            public async Task<Response> Handle(GetMonitorTaskInstancesRequest request, CancellationToken cancellationToken)
            {
                return new Response
                {
                    Instances = (await _monitorTaskHandler.GetMonitorTaskInstancesAsync(cancellationToken)).ToList()
                };
            }
        }

        public class Response
        {
            /// <summary>
            /// List of monitors with their configurations that are currently being monitored on this node.
            /// </summary>
            public List<MonitorConfigurationInstance> Instances { get; set; }
        }
    }
}
