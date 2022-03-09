using Microsoft.Extensions.Options;

using YASP.Server.Application.Authentication;
using YASP.Server.Application.Options;

namespace YASP.Server.Application.Clustering.Raft
{
    /// <summary>
    /// Handler that makes sure that all dotNext webrequests have the cluster api key appended as a header.
    /// </summary>
    public class RaftClientHandlerFactory : IHttpMessageHandlerFactory
    {
        private readonly IOptions<RootOptions> _options;

        public RaftClientHandlerFactory(IOptions<RootOptions> options)
        {
            _options = options;
        }

        public HttpMessageHandler CreateHandler(string name)
        {
            if (name == "raftClient")
            {
                return new RaftMessageHandler(
                    new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(30) },
                    _options.Value.Cluster.ApiKey);
            }

            return new SocketsHttpHandler();
        }

        public class RaftMessageHandler : MessageProcessingHandler
        {
            private readonly string _apiKey;

            public RaftMessageHandler(HttpMessageHandler innerHandler, string apiKey) : base(innerHandler)
            {
                _apiKey = apiKey;
            }

            protected override HttpRequestMessage ProcessRequest(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                request.Headers.Add(ApiKeyAuthenticationOptions.HeaderName, _apiKey);

                return request;
            }

            protected override HttpResponseMessage ProcessResponse(HttpResponseMessage response, CancellationToken cancellationToken)
            {
                return response;
            }
        }
    }
}
