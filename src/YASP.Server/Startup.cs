
using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

using System.Net;

using YASP.Server.Application.Authentication;
using YASP.Server.Application.Clustering;
using YASP.Server.Application.Clustering.Raft;
using YASP.Server.Application.Monitoring;
using YASP.Server.Application.Monitoring.Timeline;
using YASP.Server.Application.Notifications;
using YASP.Server.Application.Notifications.Email;
using YASP.Server.Application.Options;
using YASP.Server.Application.Pages;
using YASP.Server.Application.Setup;

namespace YASP.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            var rootOptions = Configuration.Get<RootOptions>();

            services.Configure<RootOptions>(Configuration);

            // Important setup services that e.g. create expected directories
            services.AddHostedService<StartupSetupService>();
            services
                .AddSingleton<RaftClusterConfigurationStorage.Bridge>()
                .AddSingleton<IHostedService>(provider => provider.GetRequiredService<RaftClusterConfigurationStorage.Bridge>());

            // Add services to the container.
            services.AddClusterServices(Configuration);

            services.AddControllersWithViews();
            services.AddRazorPages();
            services.AddMediatR(typeof(IClusterService));
            services.AddAutoMapper(typeof(IClusterService));

            // Application logic services
            services.AddSingleton<MonitorTaskDistributor>().AddSingleton<IHostedService>(provider => provider.GetRequiredService<MonitorTaskDistributor>());
            services.AddSingleton<MonitorTaskHandler>();
            services.AddTransient<MonitorCheckRunner>();
            services.AddSingleton<MonitorTimelineService>();
            services.AddSingleton<TimelineAnalyzerService>();
            services.AddSingleton<NotificationProcessor>();
            services.AddSingleton<INotificationProvider, EmailNotificationProvider>();
            services.AddSingleton<IEmailSender, DefaultEmailSender>();
            services.AddSingleton<PageValuesProvider>();

            services.AddAuthentication(ApiKeyAuthenticationOptions.Scheme)
                .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationOptions.Scheme, options => { });

            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor |
                    ForwardedHeaders.XForwardedProto |
                    ForwardedHeaders.XForwardedHost;

                options.KnownProxies.Add(IPAddress.Parse("127.0.0.1"));

                if (rootOptions.Http.Proxy.TrustAll)
                {
                    options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("0.0.0.0"), 0));
                }
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var requireSsl = app.ApplicationServices.GetRequiredService<IOptions<RootOptions>>().Value.Http.RequireSsl;

            // Accept forwarded headers from http proxy servers
            app.UseForwardedHeaders();

            // Configure the HTTP request pipeline.
            if (env.IsDevelopment())
            {
                app.UseWebAssemblyDebugging();
            }
            else
            {
                app.UseExceptionHandler("/Error");

                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                if (requireSsl) app.UseHsts();
            }

            if (requireSsl) app.UseHttpsRedirection();

            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();

            //app.UseSerilogRequestLogging();
            //app.Use(async (context, next) =>
            //{
            //    Console.WriteLine($"REQUEST STARTED: id={context.TraceIdentifier}, from={context.Request.Host}, path={context.Request.Path}");
            //    await next(context);
            //    Console.WriteLine($"REQUEST END: id={context.TraceIdentifier}, from={context.Request.Host}, path={context.Request.Path}");
            //});

            app.UseRouting();

            app.UseAuthentication();

            app.UseWhen(x => x.Request.Path.StartsWithSegments("/api"), app =>
            {
                app.UseAuthorization();
            });

            app.UseClusterService();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapClusterServices();
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapFallbackToFile("index.html");
            });
        }
    }
}
