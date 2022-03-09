using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using System.Threading.Tasks;

using YASP.Server.Application.Clustering;

namespace YASP.Server.Tests.Facts
{
    public class ClusterInterruptionTests : StableClusterTestBase
    {
        [Test]
        public async Task LeaderGoneShouldElectNewLeaderAsync()
        {
            var clusterService = Host2.Services.GetService<IClusterService>();

            var tcs = new TaskCompletionSource();

            clusterService.LeaderChanged += args =>
            {
                if (args.Node == null) return;

                if (args.Node.Endpoint == "http://localhost:5002" || args.Node.Endpoint == "http://localhost:5003")
                {
                    tcs.SetResult();
                }
            };

            // Kill leader
            await Host1.StopAsync();

            await tcs.Task.WaitAsync(_defaultTimeout);
        }
    }
}
