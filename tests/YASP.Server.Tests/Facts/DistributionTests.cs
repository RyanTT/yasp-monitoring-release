using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using YASP.Server.Application.Clustering;
using YASP.Server.Application.Persistence.Entries;
using YASP.Server.Application.Persistence.Events;

namespace YASP.Server.Tests.Facts
{
    public class DistributionTests : StableClusterTestBase
    {
        [Test]
        public async Task MonitorShouldBeDistributedToThreeNodesAsync()
        {
            // Run the actual tests
            var host1_clusterService = Host1.Services.GetService<IClusterService>();
            var host2_clusterService = Host2.Services.GetService<IClusterService>();
            var host3_clusterService = Host3.Services.GetService<IClusterService>();

            var assignmentTask = new TaskCompletionSource<ApplyMonitorNodeAssignmentEntry>();

            var entryAddedHandler = (MessageReceivedEventArgs args) =>
            {
                if (args.Entry is not ApplyMonitorNodeAssignmentEntry entry) return;

                if (entry.MonitorId == "httptest")
                {
                    assignmentTask.TrySetResult(entry);
                }
            };

            host1_clusterService.EntryAdded += entryAddedHandler;

            File.Copy("test_app.yml", Path.Combine(TESTDATA_DIRECTORY, "5001", "data", "app.yml"), overwrite: true);

            await assignmentTask.Task.WaitAsync(_defaultTimeout);

            assignmentTask.Task.Result.Nodes.Should().HaveCount(3);
        }

        [Test]
        public async Task NodeFailureShouldRedistributeMonitorToTwoNodesAsync()
        {
            // Run the actual tests
            var host1_clusterService = Host1.Services.GetService<IClusterService>();
            var host2_clusterService = Host2.Services.GetService<IClusterService>();
            var host3_clusterService = Host3.Services.GetService<IClusterService>();

            host1_clusterService.EntryAdded += args =>
            {
                if (args.Entry is ApplyMonitorNodeAssignmentEntry entry)
                {
                    Debug.WriteLine($"{entry.MonitorId} assigned to {string.Join(", ", entry.Nodes)}");
                }
            };

            {
                var assignmentTask = new TaskCompletionSource<ApplyMonitorNodeAssignmentEntry>();

                // Handler that waits until the monitor was assigned to all three nodes
                // We must make sure we check for all three nodes because the assignment might run after the second node joins and it would break this test!
                var entryAddedHandler = (MessageReceivedEventArgs args) =>
                {
                    if (args.Entry is not ApplyMonitorNodeAssignmentEntry entry) return;
                    if (entry.Nodes.Count < 3) return;

                    if (entry.MonitorId == "httptest")
                    {
                        assignmentTask.TrySetResult(entry);
                    }
                };

                host1_clusterService.EntryAdded += entryAddedHandler;

                File.Copy("test_app.yml", Path.Combine(TESTDATA_DIRECTORY, "5001", "data", "app.yml"), overwrite: true);

                await assignmentTask.Task.WaitAsync(_defaultTimeout);

                assignmentTask.Task.Result.Nodes.Should().HaveCount(3);

                host1_clusterService.EntryAdded -= entryAddedHandler;
            }

            // Kill third node
            await Host3.StopAsync();

            {
                var assignmentTask = new TaskCompletionSource<ApplyMonitorNodeAssignmentEntry>();

                var entryAddedHandler = (MessageReceivedEventArgs args) =>
                {
                    if (args.Entry is not ApplyMonitorNodeAssignmentEntry entry) return;
                    if (entry.Nodes.Count != 2) return;

                    if (entry.MonitorId == "httptest")
                    {
                        assignmentTask.TrySetResult(entry);
                    }
                };

                host1_clusterService.EntryAdded += entryAddedHandler;

                await assignmentTask.Task.WaitAsync(_defaultTimeout);

                assignmentTask.Task.Result.Nodes.Should().HaveCount(2);
            }
        }
    }
}
