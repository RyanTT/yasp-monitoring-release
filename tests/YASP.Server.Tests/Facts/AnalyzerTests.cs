using Microsoft.AspNetCore.Builder;
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
    public class AnalyzerTests : StableClusterTestBase
    {
        [Test]
        [TestCase("httptest")]
        [TestCase("tcptest")]
        public async Task MonitorShouldReportAsOfflineAsync(string monitorId)
        {
            var host1_clusterService = Host1.Services.GetService<IClusterService>();
            var host2_clusterService = Host2.Services.GetService<IClusterService>();
            var host3_clusterService = Host3.Services.GetService<IClusterService>();

            var replicatedTask = new TaskCompletionSource();
            int nodesReceivedEntry = 0;

            var entryAddedHandler = (MessageReceivedEventArgs args) =>
            {
                if (args.Entry is not ApplyMonitorStateChangeEntry applyMonitorStateChangeEntry) return;

                if (applyMonitorStateChangeEntry.MonitorId == monitorId &&
                    applyMonitorStateChangeEntry.Status == Application.Monitoring.Objects.MonitorStatusEnum.NotReachable)
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

            await replicatedTask.Task.WaitAsync(_defaultTimeout);
        }

        [Test]
        [TestCase("httptest")]
        [TestCase("tcptest")]
        public async Task MonitorShouldReportAsOnlineAsync(string monitorId)
        {
            // Create monitor target web server
            using var testApp = WebApplication.Create();
            testApp.MapGet("/", () => "Hello World!");

            _ = Task.Run(() => testApp.Run("http://localhost:6000"));

            // Run the actual tests
            var host1_clusterService = Host1.Services.GetService<IClusterService>();
            var host2_clusterService = Host2.Services.GetService<IClusterService>();
            var host3_clusterService = Host3.Services.GetService<IClusterService>();

            var replicatedTask = new TaskCompletionSource();
            int nodesReceivedEntry = 0;

            var entryAddedHandler = (MessageReceivedEventArgs args) =>
            {
                if (args.Entry is not ApplyMonitorStateChangeEntry applyMonitorStateChangeEntry) return;

                if (applyMonitorStateChangeEntry.MonitorId == monitorId &&
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

            File.Copy("test_app.yml", Path.Combine(TESTDATA_DIRECTORY, "5001", "data", "app.yml"), overwrite: true);

            await replicatedTask.Task.WaitAsync(_defaultTimeout);
        }

        [Test]
        public async Task HttpMonitorWithIncorrectKeywordShouldReportAsOfflineAsync()
        {
            // Create monitor target web server with a wrong keyword in the response
            using var testApp = WebApplication.Create();
            testApp.MapGet("/", () => "Something wrong!");

            _ = Task.Run(() => testApp.Run("http://localhost:6000"));

            // Run the actual tests
            var host1_clusterService = Host1.Services.GetService<IClusterService>();
            var host2_clusterService = Host2.Services.GetService<IClusterService>();
            var host3_clusterService = Host3.Services.GetService<IClusterService>();

            var replicatedTask = new TaskCompletionSource();
            int nodesReceivedEntry = 0;

            var entryAddedHandler = (MessageReceivedEventArgs args) =>
            {
                if (args.Entry is not ApplyMonitorStateChangeEntry applyMonitorStateChangeEntry) return;

                if (applyMonitorStateChangeEntry.MonitorId == "httptest" &&
                    applyMonitorStateChangeEntry.Status == Application.Monitoring.Objects.MonitorStatusEnum.NotReachable)
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

            await replicatedTask.Task.WaitAsync(_defaultTimeout);
        }
    }
}
