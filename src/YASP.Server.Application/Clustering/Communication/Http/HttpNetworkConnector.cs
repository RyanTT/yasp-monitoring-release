using MediatR;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using YASP.Server.Application.Authentication;
using YASP.Server.Application.Options;

namespace YASP.Server.Application.Clustering.Communication.Http
{
    /// <summary>
    /// Uses HTTP calls to transmit data between nodes as JSON.
    /// </summary>
    public class HttpNetworkConnector : INetworkConnector
    {
        /// <summary>
        /// Endpoint if the message is designated to the receiving node.
        /// </summary>
        public const string ENDPOINT = "/api/cluster/message";

        /// <summary>
        /// Endpoint if the message is designated to the leader node, but may need redirection.
        /// </summary>
        public const string ENDPOINT_ENSURE_LEADER = "/api/cluster/message-leader";

        private readonly RootOptions _options;
        private readonly IMediator _mediator;

        public HttpNetworkConnector(IOptions<RootOptions> options, IMediator mediator)
        {
            _options = options.Value;
            _mediator = mediator;
        }

        /// <summary>
        /// Registers the required routes in the endpoints builer.
        /// </summary>
        /// <param name="endpoints"></param>
        /// <returns></returns>
        public static IEndpointRouteBuilder ConfigureEndpointRouteBuilder(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost(ENDPOINT, async context => await ((HttpNetworkConnector)context.RequestServices.GetService<INetworkConnector>()).HandleAsync(context));
            endpoints.MapPost(ENDPOINT_ENSURE_LEADER, async context => await ((HttpNetworkConnector)context.RequestServices.GetService<INetworkConnector>()).HandleAsync(context));

            return endpoints;
        }

        /// <summary>
        /// Sends a request to a node.
        /// </summary>
        /// <typeparam name="TResponse">Response type</typeparam>
        /// <param name="nodeEndpoint">Receiver node</param>
        /// <param name="request">Request object</param>
        /// <param name="redirectToLeader">Should redirect to leader</param>
        /// <param name="cancellationToken">Cancel token</param>
        /// <returns></returns>
        public async Task<TResponse> SendAsync<TResponse>(NodeEndpoint nodeEndpoint, RequestBase<TResponse> request, bool redirectToLeader = false, CancellationToken cancellationToken = default)
        {
            // Calls to our own node do not need to send an actual request
            if (nodeEndpoint == _options.Cluster.ListenEndpoint && !redirectToLeader)
            {
                return await _mediator.Send(request);
            }

            var messageWrapper = new HttpNetworkMessageWrapper
            {
                Type = request.GetType().AssemblyQualifiedName,
                Content = JsonSerializer.Serialize((object)request),
            };

            var httpClient = new HttpClient();


            var str = JsonSerializer.Serialize(messageWrapper);

            httpClient.DefaultRequestHeaders.Add(ApiKeyAuthenticationOptions.HeaderName, _options.Cluster.ApiKey);

            var requestUriBuilder = new UriBuilder(nodeEndpoint.Host);
            requestUriBuilder.Path = ENDPOINT;

            if (redirectToLeader)
            {
                requestUriBuilder.Path = ENDPOINT_ENSURE_LEADER;
            }

            var response = await httpClient.PostAsJsonAsync(requestUriBuilder.ToString(), messageWrapper, cancellationToken);

            response.EnsureSuccessStatusCode();

            if (typeof(TResponse).IsAssignableTo(typeof(Unit))) return default;

            return await response.Content.ReadFromJsonAsync<TResponse>();
        }

        /// <summary>
        /// Handles a request from both the <see cref="ENDPOINT"/> and <see cref="ENDPOINT_ENSURE_LEADER"/> endpoints.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task HandleAsync(HttpContext context)
        {
            try
            {
                // Read wrapper json.
                var requestWrapper = await context.Request.ReadFromJsonAsync<HttpNetworkMessageWrapper>();

                // Get the body type of the wrapper.
                var requestContentType = Type.GetType(requestWrapper.Type);

                // Check the type is safe to deserialise into (we don't want to instantiate odd classes)
                if (!requestContentType.IsAssignableTo(typeof(IRequestBase)))
                {
                    throw new InvalidOperationException($"Content type does not inherit from {typeof(IRequestBase).Name}.");
                }

                // Deserialize
                var requestObject = JsonSerializer.Deserialize(requestWrapper.Content, Type.GetType(requestWrapper.Type));

                // Send the request into the mediator pipeline, which will look for the correct handler and execute it.
                var response = await _mediator.Send(requestObject);

                context.Response.StatusCode = (int)HttpStatusCode.OK;

                // Unit = void method => no response message
                if (response.GetType().IsAssignableTo(typeof(Unit))) return;

                await context.Response.WriteAsJsonAsync(response);

                return;
            }
            catch (Exception ex)
            {

            }

            // If we got here, then there was an error
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        }
    }
}
