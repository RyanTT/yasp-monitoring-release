
using DotNext.Buffers;
using DotNext.IO;
using DotNext.Net;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft.Membership;
using DotNext.Net.Http;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using YASP.Server.Application.Options;
using YASP.Server.Application.Utilities;

namespace YASP.Server.Application.Clustering.Raft
{
    /// <summary>
    /// Simple service that writes the last known list of nodes to disk and provides the list again upon bootup.
    /// </summary>
    public class RaftClusterConfigurationStorage
    {
        private string _lastKnownFileName;
        private readonly RootOptions _rootOptions;
        private readonly Debouncer _writeDebouncer = new Debouncer();

        public RaftClusterConfigurationStorage(IOptions<RootOptions> options, IConfiguration configuration)
        {
            var listenEndpoint = new Uri(options.Value.Cluster.ListenEndpoint);

            // Support prefixing with a configuration given path so that we may support writing and reading from a different than standard directory.
            // Required for tests!
            var workingDir = configuration.GetValue<string>("WORKING_DIRECTORY", "");
            var filePath = Path.Combine(workingDir, $"data/raft/{listenEndpoint.Host}_{listenEndpoint.Port}/ nodes.yml");

            _lastKnownFileName = filePath;
            _rootOptions = options.Value;
        }

        /// <summary>
        /// Gets the list of last known node endpoints.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns>Endpoints</returns>
        public async Task<IReadOnlyList<NodeEndpoint>> GetLastKnownEndpointsAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_lastKnownFileName))
            {
                return new List<NodeEndpoint>();
            }

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var config = deserializer.Deserialize<SerializableNodesList>(await File.ReadAllTextAsync(_lastKnownFileName, cancellationToken));

            return config.LastKnownNodes.Select(host => (NodeEndpoint)host).ToList();
        }

        /// <summary>
        /// Writes the list of all last known nodes to storage.
        /// </summary>
        /// <param name="nodes"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task WriteLastKnownEndpointsAsync(List<Node> nodes, CancellationToken cancellationToken = default)
        {
            _writeDebouncer.Throttle(TimeSpan.FromSeconds(1), async () =>
            {
                var serializer = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

                // Make sure we don't save ourselves to the list so we don't accidentally use ourself as a discovery endpoint!
                var endpoints = nodes.Select(x => x.Endpoint);//.Where(x => x != _rootOptions.Cluster.ListenEndpoint).ToList();

                // Make sure we create the directory before attempting to write or create the file, otherwise this will throw an exception
                Directory.CreateDirectory(Path.GetDirectoryName(_lastKnownFileName));

                await File.WriteAllTextAsync(_lastKnownFileName, serializer.Serialize(new SerializableNodesList
                {
                    LastKnownNodes = endpoints.Select(x => x.Host).ToArray()
                }));
            });

            return Task.CompletedTask;
        }

        public class SerializableNodesList
        {
            [ConfigurationKeyName("last_known_nodes")]
            public string[] LastKnownNodes { get; set; }
        }

        /// <summary>
        /// This class bridges the reading of nodes we last knew of into the expected data structure that dotNext expects to read from.
        /// </summary>
        /// <remarks>
        /// Implements <see cref="IHostedService"/> so that the constructor is guaranteed to run when specified in registration order.
        /// </remarks>
        public class Bridge : InMemoryClusterConfigurationStorage<HttpEndPoint>, IHostedService
        {
            public Bridge(IConfiguration configuration, RaftClusterConfigurationStorage configurationStorage)
            {
                var builder = CreateActiveConfigurationBuilder();

                // Which uri does this node listen on
                var listenUri = new Uri(configuration.GetValue<string>("cluster:listen_on"));

                // Discovery endpoints
                var discoveryEndpoints = configuration.GetSection("cluster:discovery_endpoints").Get<string[]>();

                // Get the last known nodes
                var lastKnownNodes = configurationStorage.GetLastKnownEndpointsAsync().GetAwaiter().GetResult();

                // Local node
                var localPublicEndpoint = new HttpEndPoint(listenUri);
                var clusterMemberId = ClusterMemberId.FromEndPoint(localPublicEndpoint);

                // Always put ourself into the cluster
                builder.Add(clusterMemberId, localPublicEndpoint);

                // Let dotNext know about all discovery endpoints
                if (discoveryEndpoints != null)
                {
                    foreach (var discoveryEndpoint in discoveryEndpoints)
                    {
                        var discoveryHttpEndpoint = new HttpEndPoint(new Uri((string)discoveryEndpoint));
                        builder.Add(ClusterMemberId.FromEndPoint(discoveryHttpEndpoint), discoveryHttpEndpoint);
                    }
                }

                // Let dotNext know about all previously known nodes
                foreach (var lastKnownNode in lastKnownNodes)
                {
                    var httpEndpoint = new HttpEndPoint(new Uri((string)lastKnownNode));
                    var memberId = ClusterMemberId.FromEndPoint(httpEndpoint);

                    if (!builder.ContainsKey(memberId))
                    {
                        builder.Add(memberId, httpEndpoint);
                    }
                }

                builder.Build();
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            protected override HttpEndPoint Decode(ref SequenceReader reader)
            {
                return (HttpEndPoint)reader.ReadEndPoint();
            }

            protected override void Encode(HttpEndPoint address, ref BufferWriterSlim<byte> output)
            {
                output.WriteEndPoint(address);
            }
        }
    }
}
