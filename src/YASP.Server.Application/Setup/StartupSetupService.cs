using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace YASP.Server.Application.Setup
{
    public class StartupSetupService : IHostedService
    {
        private readonly IConfiguration _configuration;

        public StartupSetupService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Support prefixing with a configuration given path so that we may support writing and reading from a different than standard directory.
            // Required for tests!
            var workingDir = _configuration.GetValue<string>("WORKING_DIRECTORY", "");
            var dataDir = Path.Combine(workingDir, "data");

            // Always make sure to create our data dir, as other components expect it to exist!
            Directory.CreateDirectory(dataDir);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
