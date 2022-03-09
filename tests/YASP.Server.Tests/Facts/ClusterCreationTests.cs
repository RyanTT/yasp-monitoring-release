using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using System.Threading.Tasks;

using YASP.Server.Application.Clustering;

namespace YASP.Server.Tests.Facts
{
    [TestFixture]
    public class ClusterCreationTests : TestBase
    {
        [Test]
        public async Task JoinClusterWithLeaderShouldWorkAsync()
        {
            using var leader = TestSetup.CreateSingleClusterHost(null, services => services.AddSingleton<TestSetup.EventTracker>());

            var leaderTracker = leader.Services.GetService<TestSetup.EventTracker>();

            TaskCompletionSource leaderElected = new TaskCompletionSource();

            leaderTracker.LeaderChanged += args =>
            {
                if (args.Node != null) leaderElected.SetResult();
            };

            await leader.StartAsync();

            // Wait for a leader to be elected
            await leaderElected.Task.WaitAsync(_defaultTimeout);


            using var follower1 = TestSetup.CreateSecondClusterHost(null, services => services.AddSingleton<TestSetup.EventTracker>());

            var followerTracker1 = follower1.Services.GetService<TestSetup.EventTracker>();

            await follower1.StartAsync();

            TaskCompletionSource nodesJoined = new TaskCompletionSource();

            leaderTracker.MemberJoined += async args =>
            {
                if ((await leader.Services.GetService<IClusterService>().GetNodesAsync()).Count == 2)
                {
                    nodesJoined.SetResult();
                }
            };

            // Either we time out and it didnt work or we reached two nodes, meaning we successfully joined the leader's cluster
            await nodesJoined.Task.WaitAsync(_defaultTimeout);
        }

        [Test]
        public async Task JoinClusterWithoutLeaderShouldWorkAsync()
        {
            using var leader = TestSetup.CreateSingleClusterHost(null, services => services.AddSingleton<TestSetup.EventTracker>());

            var leaderTracker = leader.Services.GetService<TestSetup.EventTracker>();

            TaskCompletionSource leaderElected = new TaskCompletionSource();

            leaderTracker.LeaderChanged += args =>
            {
                if (args.Node != null) leaderElected.SetResult();
            };

            await leader.StartAsync();

            // Don't wait for a leader to be elected!
            //await leaderElected.Task.WaitAsync(_defaultTimeout);


            using var follower1 = TestSetup.CreateSecondClusterHost(null, services => services.AddSingleton<TestSetup.EventTracker>());

            var followerTracker1 = follower1.Services.GetService<TestSetup.EventTracker>();

            await follower1.StartAsync();

            TaskCompletionSource nodesJoined = new TaskCompletionSource();

            leaderTracker.MemberJoined += async args =>
            {
                if ((await leader.Services.GetService<IClusterService>().GetNodesAsync()).Count == 2)
                {
                    nodesJoined.SetResult();
                }
            };

            // Either we time out and it didnt work or we reached two nodes, meaning we successfully joined the leader's cluster
            await nodesJoined.Task.WaitAsync(_defaultTimeout);
        }
    }
}