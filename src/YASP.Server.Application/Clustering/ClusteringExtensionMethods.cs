using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace YASP.Server.Application.Clustering
{
    public static class ClusteringExtensionMethods
    {
        public static IServiceCollection AddClusterServices(this IServiceCollection services, IConfiguration configuration)
        {
            return ClusterManager.ConfigureServices(services, configuration);
        }

        public static IHostBuilder UseClusterService(this IHostBuilder builder)
        {
            return ClusterManager.ConfigureHostBuilder(builder);
        }

        public static IApplicationBuilder UseClusterService(this IApplicationBuilder builder)
        {
            return ClusterManager.ConfigureApplicationBuilder(builder);
        }

        public static IEndpointRouteBuilder MapClusterServices(this IEndpointRouteBuilder endpoints)
        {
            return ClusterManager.ConfigureEndpointRouteBuilder(endpoints);
        }
    }
}
