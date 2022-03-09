using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using System.IO;
using System.Threading;
using System.Threading.Tasks;

using YASP.Server.Application.Clustering;
using YASP.Server.Application.Persistence.Entries;
using YASP.Server.Application.Persistence.Events;

namespace YASP.Server.Tests.Facts
{
    public class ConfigReplicationTests : StableClusterTestBase
    {
        [Test]
        public async Task ConfigChangeOnLeaderShouldReplicateConfigAsync()
        {
            var host1_clusterService = Host1.Services.GetService<IClusterService>();
            var host2_clusterService = Host2.Services.GetService<IClusterService>();
            var host3_clusterService = Host3.Services.GetService<IClusterService>();

            int nodesReceivedConfig = 0;
            var replicatedTask = new TaskCompletionSource();

            var entryAddedHandler = (MessageReceivedEventArgs args) =>
            {
                if (args.Entry is ApplyConfigurationEntry configEntry && configEntry.AppConfiguration.Monitors.Count > 0)
                {
                    Interlocked.Increment(ref nodesReceivedConfig);

                    if (nodesReceivedConfig == 3)
                    {
                        replicatedTask.SetResult();
                    }
                }
            };

            host1_clusterService.EntryAdded += entryAddedHandler;
            host2_clusterService.EntryAdded += entryAddedHandler;
            host3_clusterService.EntryAdded += entryAddedHandler;

            File.Copy("test_app.yml", Path.Combine(TESTDATA_DIRECTORY, "5001", "data", "app.yml"), overwrite: true);

            await replicatedTask.Task.WaitAsync(_defaultTimeout);
        }

        [Test]
        public async Task ConfigChangeOnFollowerShouldReplicateConfigAsync()
        {
            var host1_clusterService = Host1.Services.GetService<IClusterService>();
            var host2_clusterService = Host2.Services.GetService<IClusterService>();
            var host3_clusterService = Host3.Services.GetService<IClusterService>();

            int nodesReceivedConfig = 0;
            var replicatedTask = new TaskCompletionSource();

            var entryAddedHandler = (MessageReceivedEventArgs args) =>
            {
                if (args.Entry is ApplyConfigurationEntry configEntry && configEntry.AppConfiguration.Monitors.Count == 2)
                {
                    Interlocked.Increment(ref nodesReceivedConfig);

                    if (nodesReceivedConfig == 3)
                    {
                        replicatedTask.SetResult();
                    }
                }
            };

            host1_clusterService.EntryAdded += entryAddedHandler;
            host2_clusterService.EntryAdded += entryAddedHandler;
            host3_clusterService.EntryAdded += entryAddedHandler;

            File.Copy("test_app.yml", Path.Combine(TESTDATA_DIRECTORY, "5002", "data", "app.yml"), overwrite: true);

            await replicatedTask.Task.WaitAsync(_defaultTimeout);
        }
    }
}
