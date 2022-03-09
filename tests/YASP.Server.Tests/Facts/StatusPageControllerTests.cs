
using FluentAssertions;

using Microsoft.AspNetCore.Builder;

using NUnit.Framework;

using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

using YASP.Shared.Objects;

namespace YASP.Server.Tests.Facts
{
    public class StatusPageControllerTests : StableClusterTestBase
    {
        [Test]
        public async Task InvalidIdShouldReturn404Async()
        {
            // Create monitor target web server so we can also check for the status inside the controller returned data
            using var testApp = WebApplication.Create();
            testApp.MapGet("/", () => "Hello world!");

            _ = Task.Run(() => testApp.Run("http://localhost:6000"));

            await WaitForConfigurationReplicationAsync();

            var hosts = new[] {
                "http://localhost:5001",
                "http://localhost:5002",
                "http://localhost:5003"
            };

            foreach (var host in hosts)
            {
                var httpClient = new HttpClient()
                {
                    BaseAddress = new System.Uri(host)
                };

                var request = new HttpRequestMessage(HttpMethod.Get, $"/api/pages/idontexist");

                var response = await httpClient.SendAsync(request);

                response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
            }
        }

        [Test]
        public async Task ValidIdShouldReturnCorrectDataAsync()
        {
            // Create monitor target web server so we can also check for the status inside the controller returned data
            using var testApp = WebApplication.Create();
            testApp.MapGet("/", () => "Hello world!");

            _ = Task.Run(() => testApp.Run("http://localhost:6000"));

            await WaitForConfigurationReplicationAsync();
            await WaitForMonitorResultsToBeWrittenToLogAsync();

            var hosts = new[] {
                "http://localhost:5001",
                "http://localhost:5002",
                "http://localhost:5003"
            };

            foreach (var host in hosts)
            {
                var httpClient = new HttpClient()
                {
                    BaseAddress = new System.Uri(host)
                };

                var request = new HttpRequestMessage(HttpMethod.Get, $"/api/pages/test");

                var response = await httpClient.SendAsync(request);

                response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

                var data = await response.Content.ReadFromJsonAsync<PageDto>();

                data.Categories.Should().HaveCount(1);
                data.Categories.First().Monitors.Should().HaveCount(1);
                data.Categories.First().Monitors.Select(x => x.MonitorId).Should().Equal(new[] { "httptest" });
                data.Categories.First().Monitors.First().States.Should().HaveCount(1);
                data.Categories.First().Monitors.First().States.First().MonitorStatus.Should().Be(MonitorStatusEnumDto.Reachable);
            }
        }
    }
}
