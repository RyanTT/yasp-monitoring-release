using DotNext.Net.Http;

namespace YASP.Server.Application.Clustering.Raft
{
    public static class RaftExtensions
    {
        public static HttpEndPoint AsHttpEndPoint(this NodeEndpoint nodeEndpoint) => new HttpEndPoint(new Uri(nodeEndpoint.Host));
        public static NodeEndpoint AsNodeEndpoint(this HttpEndPoint httpEndPoint) => ((UriBuilder)httpEndPoint).Uri.ToString();
    }
}
