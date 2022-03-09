using MimeKit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using YASP.Server.Application.Notifications.Email;

namespace YASP.Server.Tests
{
    public class TestEmailSender : IEmailSender
    {
        public bool ShouldError { get; set; }
        public event Action<MimeMessage> MessageSent;

        public List<MimeMessage> OutgoingMails
        {
            get
            {
                lock (_outgoingMails)
                {
                    return _outgoingMails.ToList();
                }
            }
        }

        private List<MimeMessage> _outgoingMails = new List<MimeMessage>();

        public Task SendAsync(MimeMessage message, string host, int port, string username, string password, CancellationToken cancellationToken = default)
        {
            if (ShouldError) throw new System.Exception("TestEmailSender failed on purpose.");

            lock (OutgoingMails)
            {
                OutgoingMails.Add(message);
            }

            MessageSent?.Invoke(message);

            return Task.CompletedTask;
        }
    }
}
