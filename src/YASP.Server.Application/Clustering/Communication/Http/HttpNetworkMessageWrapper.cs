namespace YASP.Server.Application.Clustering.Communication.Http
{
    public class HttpNetworkMessageWrapper
    {
        /// <summary>
        /// .NET type of the <see cref="Content"/>.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Content JSON string.
        /// </summary>
        public string Content { get; set; }
    }
}
