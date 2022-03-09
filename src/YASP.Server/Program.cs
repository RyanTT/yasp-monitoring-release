using Serilog;

using System.Diagnostics.CodeAnalysis;

using YASP.Server;
using YASP.Server.Application.Clustering;

[ExcludeFromCodeCoverage(Justification = "Not captured in tests.")]
public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        IConfiguration configInstance;

        return Host.CreateDefaultBuilder(args)
            .UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration, "logging")
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
            )
            .ConfigureAppConfiguration((hostBuilder, configuration) => configuration
                .AddEnvironmentVariables()
                .AddYamlFile("./data/node.yml")
                .AddYamlFile($"./data/node.{hostBuilder.HostingEnvironment.EnvironmentName}.yml", optional: true)
                .AddCommandLine(args))
            .ConfigureHostOptions((hostBuilderContext, hostOptions) =>
            {
                configInstance = hostBuilderContext.Configuration;
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder
                    .UseStartup<Startup>();
            })
            .ConfigureLogging((logging) =>
            {
                //logging.SetMinimumLevel(LogLevel.Information);
                //logging.AddFilter("Microsoft", LogLevel.Error);
                //logging.AddFilter("DotNext", LogLevel.Trace);
            })
            .UseClusterService();
    }
}