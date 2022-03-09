
using MimeKit;

namespace YASP.Server.Application.Notifications.Email
{
    /// <summary>
    /// Interface to abstract of sending an email. Used to disable sending emails in a test scenario.
    /// </summary>
    public interface IEmailSender
    {
        /// <summary>
        /// Sends an email via the specificed mail server.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="host">Mail server host.</param>
        /// <param name="port">Mail server port.</param>
        /// <param name="username">Mail server username.</param>
        /// <param name="password">Mail server password.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns></returns>
        Task SendAsync(MimeMessage message, string host, int port, string username, string password, CancellationToken cancellationToken = default);
    }
}
