using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.IO;

using YASP.Server.Application.Clustering;
using YASP.Server.Application.Clustering.Discovery.Events;
using YASP.Server.Application.Notifications.Email;

namespace YASP.Server.Tests
{
    public static class TestSetup
    {
        public class EventTracker
        {
            private readonly IClusterService _clusterService;
            public event Action<MemberJoinedEventArgs> MemberJoined;
            public event Action<MemberLeftEventArgs> MemberLeft;
            public event Action<LeaderChangedEventArgs> LeaderChanged;

            public EventTracker(IClusterService clusterService)
            {
                _clusterService = clusterService;

                _clusterService.MemberJoined += args => MemberJoined?.Invoke(args);
                _clusterService.MemberLeft += args => MemberLeft?.Invoke(args);
                _clusterService.LeaderChanged += args => LeaderChanged?.Invoke(args);
            }
        }

        public static IHost CreateHost<TStartup>(int port, IDictionary<string, string> configuration, Action<IServiceCollection> serviceConfigurator) where TStartup : class
        {
            configuration.TryAdd("WORKING_DIRECTORY", Path.Combine(TestBase.TESTDATA_DIRECTORY, port.ToString()));
            configuration.TryAdd("DOTNEXT_HEARTBEAT_THRESHOLD", "0.1");
            configuration.TryAdd("DOTNEXT_LOWER_TIMEOUT", "5000");
            configuration.TryAdd("DOTNEXT_UPPER_TIMEOUT", "10000");


            return new HostBuilder()
                .ConfigureWebHost(webHost => webHost.UseKestrel(options => options.ListenLocalhost(port))
                    .UseStartup<Startup>()
                    .ConfigureServices(services =>
                    {
                        // Replace the actual email sender with out test implementation
                        services.Replace(ServiceDescriptor.Singleton<IEmailSender, TestEmailSender>());

                        if (serviceConfigurator != null)
                        {
                            serviceConfigurator.Invoke(services);
                        }
                    })
                )
                    .UseClusterService()
                    .ConfigureAppConfiguration((hostBuilder, configBuilder) => configBuilder
                    .AddInMemoryCollection(configuration))
                .ConfigureLogging(builder => builder
                    .SetMinimumLevel(LogLevel.Information)
                    .AddConsole(options => { })
                    .AddDebug())
                .Build();

        }

        public static IHost CreateSingleClusterHost(Action<Dictionary<string, string>> configConfiguration, Action<IServiceCollection> serviceConfigurator)
        {
            var config = new Dictionary<string, string>
            {
                { "http:require_ssl", "false" },
                { "cluster:api_key", "default" },
                { "cluster:listen_on", $"http://localhost:5001" }
            };

            configConfiguration?.Invoke(config);

            return CreateHost<Startup>(5001, config, serviceConfigurator);
        }

        public static IHost CreateSecondClusterHost(Action<Dictionary<string, string>> configConfiguration, Action<IServiceCollection> serviceConfigurator)
        {
            var config = new Dictionary<string, string>
            {
                { "http:require_ssl", "false" },
                { "cluster:api_key", "default" },
                { "cluster:listen_on", $"http://localhost:5002" },
                { "cluster:discovery_endpoints:0", "http://localhost:5001" }
            };

            configConfiguration?.Invoke(config);

            return CreateHost<Startup>(5002, config, serviceConfigurator);
        }

        public static IHost CreateThirdClusterHost(Action<Dictionary<string, string>> configConfiguration, Action<IServiceCollection> serviceConfigurator)
        {
            var config = new Dictionary<string, string>
            {
                { "http:require_ssl", "false" },
                { "cluster:api_key", "default" },
                { "cluster:listen_on", $"http://localhost:5003" },
                { "cluster:discovery_endpoints:0", "http://localhost:5001" }
            };

            configConfiguration?.Invoke(config);

            return CreateHost<Startup>(5003, config, serviceConfigurator);
        }
    }
}
