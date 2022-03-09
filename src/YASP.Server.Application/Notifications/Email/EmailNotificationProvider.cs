
using Microsoft.Extensions.Logging;

using MimeKit;

using YASP.Server.Application.Monitoring.Objects;
using YASP.Server.Application.Persistence.Objects;
using YASP.Server.Application.State;

namespace YASP.Server.Application.Notifications.Email
{
    /// <summary>
    /// Provider that sends emails out as notifications.
    /// </summary>
    public class EmailNotificationProvider : INotificationProvider
    {
        public string Identifier => "Email";

        private readonly ILogger<EmailNotificationProvider> _logger;
        private readonly IEmailSender _emailSender;
        private readonly ApplicationStateService _applicationStateService;

        public EmailNotificationProvider(ILogger<EmailNotificationProvider> logger, IEmailSender emailSender, ApplicationStateService applicationStateService)
        {
            _logger = logger;
            _emailSender = emailSender;
            _applicationStateService = applicationStateService;
        }


        public async Task<bool> SendNotificationAsync(MonitorState state, CancellationToken cancellationToken = default)
        {
            try
            {
                // Get a snapshot of the application state to fetch the email settings
                var appState = await _applicationStateService.GetSnapshotAsync();
                var emailSettings = appState.AppConfiguration.Notifications.Email;

                var message = new MimeMessage();

                var statusString = state.MonitorStatus switch
                {
                    MonitorStatusEnum.Unknown => "Unknown",
                    MonitorStatusEnum.Reachable => "Online",
                    MonitorStatusEnum.NotReachable => "Offline",
                    MonitorStatusEnum.PartiallyReachable => "Partially Online",
                    _ => "",
                };

                // Sender
                message.From.Add(new MailboxAddress(emailSettings.SenderDisplayName, emailSettings.SenderAddress));

                // Targets
                foreach (var recipient in emailSettings.Recipients)
                {
                    message.To.Add(new MailboxAddress(recipient.Key, recipient.Key));
                }

                var combinedMessage = new MultipartAlternative
            {
                new TextPart(MimeKit.Text.TextFormat.Html)
                {
                    Text = $"A check executed at {state.CheckTimestamp:dd.MM.yyyy HH:mm} (UTC) has determined the status of the monitor '{appState.AppConfiguration.Monitors.FirstOrDefault(x => x.Id == state.MonitorId)?.DisplayName ?? state.MonitorId}' " +
                        $"to be '{statusString}'."
                }
            };

                message.Body = combinedMessage;

                message.Subject = $"Status of '{appState.AppConfiguration.Monitors.FirstOrDefault(x => x.Id == state.MonitorId)?.DisplayName ?? state.MonitorId}' " +
                    $"has changed to '{statusString}' since {state.CheckTimestamp:dd.MM.yyyy HH:mm} (UTC)";

                await _emailSender.SendAsync(message, emailSettings.Host, emailSettings.Port, emailSettings.Username, emailSettings.Password, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send notification for {state.MonitorId}!");

                throw;
            }

            return true;
        }

        public async Task<bool> HandlesMonitorAsync(MonitorId monitorId, CancellationToken cancellationToken = default)
        {
            var appState = await _applicationStateService.GetSnapshotAsync();

            return appState.AppConfiguration.Notifications.Email.Recipients.Any(x => x.Value.Contains((string)monitorId));
        }
    }
}
