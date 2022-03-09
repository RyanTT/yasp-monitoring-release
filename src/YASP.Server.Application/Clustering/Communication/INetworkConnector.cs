namespace YASP.Server.Application.Clustering.Communication
{
    /// <summary>
    /// Network Connector used to send messages to other nodes of the cluster.
    /// </summary>
    public interface INetworkConnector
    {
        /// <summary>
        /// Sends a request to a <paramref name="nodeEndpoint"/> and optionally instructs it to redirect to the leader if necessary..
        /// </summary>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="nodeEndpoint">Target node</param>
        /// <param name="request">Request body</param>
        /// <param name="redirectToLeader">Should the target node attempt to redirect to the leader</param>
        /// <param name="cancellationToken">Cancel token</param>
        /// <returns></returns>
        Task<TResponse> SendAsync<TResponse>(NodeEndpoint nodeEndpoint,
            RequestBase<TResponse> request,
            bool redirectToLeader = false,
            CancellationToken cancellationToken = default);
    }
}
