using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NUnit.Framework;

using System.IO;
using System.Threading;
using System.Threading.Tasks;

using YASP.Server.Application.Clustering;
using YASP.Server.Application.Persistence.Entries;
using YASP.Server.Application.Persistence.Events;

namespace YASP.Server.Tests
{
    public class StableClusterTestBase : TestBase
    {
        public IHost Host1 { get; set; }
        public IHost Host2 { get; set; }
        public IHost Host3 { get; set; }

        [SetUp]
        public async Task SetupClusterAsync()
        {
            Host1 = TestSetup.CreateSingleClusterHost(null, services => services.AddSingleton<TestSetup.EventTracker>());

            var host1Tracker = Host1.Services.GetService<TestSetup.EventTracker>();

            TaskCompletionSource leaderElected = new TaskCompletionSource();

            host1Tracker.LeaderChanged += args =>
            {
                if (args.Node != null) leaderElected.SetResult();
            };

            await Host1.StartAsync();

            // Wait for a leader to be elected
            await leaderElected.Task.WaitAsync(_defaultTimeout);


            // Start node 2
            Host2 = TestSetup.CreateSecondClusterHost(null, services => services.AddSingleton<TestSetup.EventTracker>());

            var node2HasLeader = new TaskCompletionSource();
            var node2KnowsAllNodes = new TaskCompletionSource();

            var host2Tracker = Host2.Services.GetService<TestSetup.EventTracker>();

            host2Tracker.LeaderChanged += args =>
            {
                if (args.Node != null)
                {
                    node2HasLeader.TrySetResult();
                }
            };

            host2Tracker.MemberJoined += args =>
            {
                if (Host2.Services.GetService<IClusterService>().GetNodesAsync().Result.Count == 3)
                {
                    node2KnowsAllNodes.TrySetResult();
                }
            };

            await Host2.StartAsync();
            await node2HasLeader.Task.WaitAsync(_defaultTimeout);

            // Start node 3
            Host3 = TestSetup.CreateThirdClusterHost(null, services => services.AddSingleton<TestSetup.EventTracker>());

            var node3HasLeader = new TaskCompletionSource();
            var node3KnowsAllNodes = new TaskCompletionSource();

            var host3Tracker = Host3.Services.GetService<TestSetup.EventTracker>();

            host3Tracker.LeaderChanged += args =>
            {
                if (args.Node != null)
                {
                    node3HasLeader.TrySetResult();
                }
            };

            host3Tracker.MemberJoined += args =>
            {
                if (Host3.Services.GetService<IClusterService>().GetNodesAsync().Result.Count == 3)
                {
                    node3KnowsAllNodes.TrySetResult();
                }
            };


            await Host3.StartAsync();
            await node3HasLeader.Task.WaitAsync(_defaultTimeout);

            await Task.WhenAll(node2KnowsAllNodes.Task, node3KnowsAllNodes.Task).WaitAsync(_defaultTimeout);
        }

        [TearDown]
        public Task TeardownClusterAsync()
        {
            Host1?.Dispose();
            Host2?.Dispose();
            Host3?.Dispose();

            return Task.CompletedTask;
        }

        public async Task WaitForConfigurationReplicationAsync()
        {
            var host1_clusterService = Host1.Services.GetService<IClusterService>();
            var host2_clusterService = Host2.Services.GetService<IClusterService>();
            var host3_clusterService = Host3.Services.GetService<IClusterService>();

            var replicatedTask = new TaskCompletionSource();
            int nodesReceivedEntry = 0;

            var entryAddedHandler = (MessageReceivedEventArgs args) =>
            {
                if (args.Entry is ApplyConfigurationEntry configEntry && configEntry.AppConfiguration.Monitors.Count == 2)
                {
                    Interlocked.Increment(ref nodesReceivedEntry);

                    if (nodesReceivedEntry == 3)
                    {
                        replicatedTask.SetResult();
                    }
                }
            };

            host1_clusterService.EntryAdded += entryAddedHandler;
            host2_clusterService.EntryAdded += entryAddedHandler;
            host3_clusterService.EntryAdded += entryAddedHandler;

            File.Copy("test_app.yml", Path.Combine(TESTDATA_DIRECTORY, "5001", "data", "app.yml"), overwrite: true);

            // Wait until the config has been replicated to everyone
            await replicatedTask.Task.WaitAsync(_defaultTimeout);
        }

        public async Task WaitForMonitorResultsToBeWrittenToLogAsync()
        {
            var host1_clusterService = Host1.Services.GetService<IClusterService>();
            var host2_clusterService = Host2.Services.GetService<IClusterService>();
            var host3_clusterService = Host3.Services.GetService<IClusterService>();

            var replicatedTask = new TaskCompletionSource();
            int nodesReceivedEntry = 0;

            var entryAddedHandler = (MessageReceivedEventArgs args) =>
            {
                if (args.Entry is not ApplyMonitorStateChangeEntry applyMonitorStateChangeEntry) return;

                if (applyMonitorStateChangeEntry.MonitorId == "httptest" &&
                    applyMonitorStateChangeEntry.Status == Application.Monitoring.Objects.MonitorStatusEnum.Reachable)
                {
                    Interlocked.Increment(ref nodesReceivedEntry);

                    if (nodesReceivedEntry == 3)
                    {
                        replicatedTask.SetResult();
                    }
                }
            };

            host1_clusterService.EntryAdded += entryAddedHandler;
            host2_clusterService.EntryAdded += entryAddedHandler;
            host3_clusterService.EntryAdded += entryAddedHandler;

            await replicatedTask.Task.WaitAsync(_defaultTimeout);
        }
    }
}
