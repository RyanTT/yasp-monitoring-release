using MediatR;

using YASP.Server.Application.Clustering.Communication;

namespace YASP.Server.Application.Clustering.Discovery.Messages
{
    /// <summary>
    /// Network request to join a cluster. Must be delivered to the leader.
    /// </summary>
    public class JoinClusterRequest : RequestBase<JoinClusterRequest.Response>
    {
        /// <summary>
        /// Endpoint of the node attempting to join.
        /// </summary>
        public string Endpoint { get; set; }

        public class Handler : IRequestHandler<JoinClusterRequest, Response>
        {
            private readonly IClusterService _clusterService;

            public Handler(IClusterService clusterService)
            {
                _clusterService = clusterService;
            }

            public async Task<Response> Handle(JoinClusterRequest request, CancellationToken cancellationToken)
            {
                var success = await _clusterService.AddNodeAsync(request.Endpoint, cancellationToken);

                return new Response
                {
                    Success = success
                };
            }
        }

        public class Response
        {
            public bool Success { get; set; }
        }
    }
}
