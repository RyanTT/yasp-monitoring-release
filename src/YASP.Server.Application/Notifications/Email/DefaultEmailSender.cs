using MailKit.Net.Smtp;

using MimeKit;

namespace YASP.Server.Application.Notifications.Email
{
    /// <inheritdoc/>
    public class DefaultEmailSender : IEmailSender
    {
        /// <inheritdoc/>
        public async Task SendAsync(MimeMessage message, string host, int port, string username, string password, CancellationToken cancellationToken = default)
        {
            using (var client = new SmtpClient())
            {
                await client.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.Auto, cancellationToken);

                await client.AuthenticateAsync(username, password, cancellationToken);

                await client.SendAsync(message);

                await client.DisconnectAsync(true, cancellationToken);
            }
        }
    }
}
