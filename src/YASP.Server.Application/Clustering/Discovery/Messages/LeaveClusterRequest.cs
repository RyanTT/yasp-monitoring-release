using MediatR;

using YASP.Server.Application.Clustering.Communication;

namespace YASP.Server.Application.Clustering.Discovery.Messages
{
    /// <summary>
    /// Network request for a node to gracefully leave the cluster.
    /// </summary>
    public class LeaveClusterRequest : RequestBase<Unit>
    {
        /// <summary>
        /// Endpoint of the node to leave.
        /// </summary>
        public string Endpoint { get; set; }

        public class Handler : IRequestHandler<LeaveClusterRequest>
        {
            private readonly IClusterService _clusterService;

            public Handler(IClusterService clusterService)
            {
                _clusterService = clusterService;
            }

            public async Task<Unit> Handle(LeaveClusterRequest request, CancellationToken cancellationToken)
            {
                await _clusterService.RemoveNodeAsync(request.Endpoint);

                return Unit.Value;
            }
        }
    }
}
