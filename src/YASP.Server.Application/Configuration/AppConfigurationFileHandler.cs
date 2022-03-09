using Cronos;

using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using YASP.Server.Application.Clustering;
using YASP.Server.Application.Configuration.Messages;
using YASP.Server.Application.Monitoring.Objects;
using YASP.Server.Application.Persistence.Entries;
using YASP.Server.Application.Utilities;

namespace YASP.Server.Application.Configuration
{
    /// <summary>
    /// Service that monitors the local app configuration file and proposes edits to the cluster leader aswell as save new applied configurations to the local file.
    /// </summary>
    public class AppConfigurationFileHandler : IHostedService
    {
        private FileSystemWatcher _watcher;
        private readonly IMediator _mediator;
        private readonly IClusterService _clusterService;
        private readonly ILogger<AppConfigurationFileHandler> _logger;
        private readonly Debouncer _writeDebouncer = new Debouncer();
        private string _fileName = "./data/app.yml";
        private readonly Nito.AsyncEx.AsyncLock _writeLock = new Nito.AsyncEx.AsyncLock();
        private Task _proposeTask = default;

        public AppConfiguration AppConfiguration { get; private set; }

        public AppConfigurationFileHandler(IMediator mediator, IConfiguration configuration, IClusterService clusterService, ILogger<AppConfigurationFileHandler> logger)
        {
            _mediator = mediator;
            _clusterService = clusterService;
            _logger = logger;
            _clusterService.EntryAdded += _clusterService_MessageReceived;

            AppConfiguration = new AppConfiguration
            {
                Revision = 0,
                Monitors = new List<MonitorConfiguration>()
            };

            // Support prefixing with a configuration given path so that we may support writing and reading from a different than standard directory.
            // Required for tests!
            var workingDir = configuration.GetValue("WORKING_DIRECTORY", "");
            var filePath = Path.Combine(workingDir, $"data/app.yml");

            _fileName = filePath;
        }

        /// <summary>
        /// Listens on Raft log entries. If a snapshot or app configuration is received, we save the app configuration to the local file.
        /// </summary>
        /// <param name="args"></param>
        private async void _clusterService_MessageReceived(Persistence.Events.MessageReceivedEventArgs args)
        {
            // Both ConfigurationEntry and ApplicationStateSnapshot hold an AppConfiguration object
            if (args.Entry is ApplyConfigurationEntry || args.Entry is ApplicationStateSnapshot)
            {
                AppConfiguration config = default;

                if (args.Entry is ApplyConfigurationEntry entry) config = entry.AppConfiguration;
                if (args.Entry is ApplicationStateSnapshot snapshotEntry) config = snapshotEntry.AppConfiguration;

                AppConfiguration = config;

                _ = Task.Run(async () =>
                {
                    if (_proposeTask != null && !_proposeTask.IsCompleted)
                    {
                        // While we are proposing a new file, we do not want to overwrite the local file, hence we should wait for the result.
                        await _proposeTask;
                    }

                    // When the local Raft log is being loaded, this event may fire multiple times. Debounce the calls and wait a second before actually writing to disk.
                    _writeDebouncer.Throttle(TimeSpan.FromSeconds(1), async () =>
                    {
                        using var l = await _writeLock.LockAsync();

                        // Disable the file watcher so we don't accidentally fire because of us writing to disk.
                        _watcher.EnableRaisingEvents = false;

                        _logger.LogInformation($"Persisted application configuration with revision={config.Revision}.");
                        await WriteConfigurationAsync(config);

                        _watcher.EnableRaisingEvents = true;
                    });
                });
            }
        }

        /// <summary>
        /// Called on the leader to handle a new proposed <paramref name="proposedConfiguration"/>.
        /// </summary>
        /// <param name="proposedConfiguration">Configuration the be proposed to the cluster.</param>
        /// <param name="forceOnEqualRevision">True if the configuration should be applied if the revision is the same as the currently active revision. Otherwise it must be greater.</param>
        /// <returns></returns>
        public async Task HandleProposedConfigurationAsync(AppConfiguration proposedConfiguration, bool forceOnEqualRevision)
        {
            _logger.LogDebug($"Configuration is being proposed to the leader");

            if (proposedConfiguration.Revision < AppConfiguration.Revision)
            {
                _logger.LogDebug($"Proposed configuration was of a lower revision, dismissed.");

                return;
            }

            if (!forceOnEqualRevision && proposedConfiguration.Revision == AppConfiguration.Revision)
            {
                _logger.LogDebug($"Proposed configuration was of equal revision, but forceOnEqualRevision was false, dismissed.");

                return;
            }

            // Write the proposed config to the Raft log and thus make it our active configuration
            _logger.LogDebug($"Writing new configuration to storage..");
            await _clusterService.WriteAsync(new ApplyConfigurationEntry { AppConfiguration = proposedConfiguration });
            _logger.LogDebug($"Configuration written to storage!");
        }

        /// <summary>
        /// Sets up the <see cref="FileSystemWatcher"/> to watch the local app configuration file.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(_fileName), "app.yml");
            _watcher.Changed += _watcher_Changed;
            _watcher.EnableRaisingEvents = true;

            _logger.LogDebug($"Checking for {_fileName} on startup..");
            _logger.LogDebug($"Expected absolute path: {Path.GetFullPath(_fileName)}");

            if (File.Exists(_fileName))
            {
                _logger.LogDebug($"{_fileName} exists. Parsing config and proposing to leader.");

                try
                {
                    var config = await ReadConfigAsync(cancellationToken);

                    _proposeTask = Task.Run(() => ProposeConfigurationAsync(config));

                    AppConfiguration = config;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to read local {_fileName}. Using default configuration. ({ex.Message})");
                    AppConfiguration = new AppConfiguration();
                }
            }
            else
            {
                AppConfiguration = new AppConfiguration();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Proposes an app configuration to the leader until the transmission was successful.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task ProposeConfigurationAsync(AppConfiguration config, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                try
                {
                    await _clusterService.SendToLeaderAsync(new ProposeConfigurationRequest
                    {
                        ProposedConfiguration = config,
                        ForceOnEqualRevision = await _clusterService.IsNodeLeaderAsync(null) // If we are the leader node, we can force changes with revision >= currentRevision
                    });

                    break;
                }
                catch
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));
                }
            }
        }

        /// <summary>
        /// Loads the <see cref="AppConfiguration"/> from the local file and calls <see cref="ProposeConfigurationAsync(AppConfiguration, CancellationToken)"/>.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task RefreshFromFileAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(_fileName)) return;

                var config = await ReadConfigAsync(cancellationToken);

                _proposeTask = Task.Run(() => ProposeConfigurationAsync(config, cancellationToken));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Couldn't load {_fileName}. ({ex.Message})");
            }
        }

        /// <summary>
        /// Attemps to read the local app configuration file and returns it.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<AppConfiguration> ReadConfigAsync(CancellationToken cancellationToken = default)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            const int maxRetries = 3;

            for (int i = 1; i <= maxRetries; i++)
            {
                try
                {
                    var config = deserializer.Deserialize<AppConfiguration>(await File.ReadAllTextAsync(_fileName, cancellationToken));

                    ValidateConfig(config);

                    return config;
                }
                catch
                {
                    if (i == maxRetries) throw;

                    await Task.Delay(100);
                }
            }

            throw new Exception($"Cannot read {_fileName}.");
        }

        /// <summary>
        /// Attempts to perform basic validation on the values of the given <paramref name="appConfiguration"/> and throws a <see cref="InvalidDataException"/> if invalid.
        /// </summary>
        /// <param name="appConfiguration"></param>
        /// <exception cref="InvalidDataException"></exception>
        public void ValidateConfig(AppConfiguration appConfiguration)
        {
            foreach (var monitor in appConfiguration.Monitors)
            {
                if (string.IsNullOrEmpty(monitor.Id)) Throw($"\"monitors.id\" cannot be empty.");

                try
                {
                    var expr = CronExpression.Parse(monitor.Interval, CronFormat.IncludeSeconds);

                    if (expr == null) throw new Exception();
                }
                catch
                {
                    Throw($"\"monitors.interval\" value \"{monitor.Interval}\" is not a valid CRON expression.");
                }

                if (monitor.CheckWith < 0) Throw($"\"monitors.check_with\" cannot be less than 0.");
                if (monitor.PartialThreshold > monitor.AvailableThreshold) Throw($"\"monitors.partial_threshold\" must be equal or smaller than \"monitors.available_threshold\".");
                if (monitor.Timeout <= 0) Throw($"\"monitors.timeout\" must be greater than 0.");
                if (monitor.Http == null && monitor.Tcp == null) Throw($"No monitor type has been specified.");

                if (monitor.Http != null)
                {
                    if (string.IsNullOrEmpty(monitor.Http.Url)) Throw($"\"monitors.http.url\" cannot be empty.");
                    if (!new[] { "get", "head" }.Contains(monitor.Http.Method)) Throw($"\"monitors.http.method\" must be get or head.");

                    try
                    {
                        new Uri(monitor.Http.Url);
                    }
                    catch
                    {
                        Throw($"\"monitors.http.url\" is not a valid url.");
                    }
                }

                if (monitor.Tcp != null)
                {

                }
            }

            void Throw(string message)
            {
                throw new InvalidDataException($"Failed to validate {_fileName}: {message}");
            }
        }

        /// <summary>
        /// Writes the <paramref name="config"/> to the local file.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task WriteConfigurationAsync(AppConfiguration config, CancellationToken cancellationToken = default)
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            const int retries = 3;

            for (int i = 0; i < retries; i++)
            {
                try
                {
                    await File.WriteAllTextAsync(_fileName, serializer.Serialize(config));

                    return;
                }
                catch
                {
                    await Task.Delay(100);
                }
            }

            _logger.LogWarning($"Failed to write {_fileName}, attempted {retries} times.");
        }

        /// <summary>
        /// Event raised by the <see cref="_watcher"/> when the local app configuration file was edited.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void _watcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType != WatcherChangeTypes.Changed) return;

            _ = Task.Run(async () => await RefreshFromFileAsync());
        }

    }
}
