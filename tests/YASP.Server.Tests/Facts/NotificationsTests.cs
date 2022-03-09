using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using MimeKit;

using NUnit.Framework;

using System.Threading.Tasks;

using YASP.Server.Application.Notifications.Email;

namespace YASP.Server.Tests.Facts
{
    public class NotificationsTests : StableClusterTestBase
    {
        [Test]
        public async Task UnavailableMonitorShouldSendOfflineMailAsync()
        {
            var mailSender = Host1.Services.GetService<IEmailSender>() as TestEmailSender;

            var messageSentTcs = new TaskCompletionSource<MimeMessage>();

            mailSender.MessageSent += message =>
            {
                if (message.ToString().Contains("HTTP Test") && message.ToString().Contains("Offline"))
                {
                    messageSentTcs.SetResult(message);
                }
            };

            await WaitForConfigurationReplicationAsync();

            await messageSentTcs.Task.WaitAsync(_defaultTimeout);
        }

        [Test]
        public async Task AvailableMonitorShouldSendOnlineMailAsync()
        {
            // Create monitor target web server
            using var testApp = WebApplication.Create();
            testApp.MapGet("/", () => "Hello world!");

            _ = Task.Run(() => testApp.Run("http://localhost:6000"));


            var mailSender = Host1.Services.GetService<IEmailSender>() as TestEmailSender;

            var messageSentTcs = new TaskCompletionSource<MimeMessage>();

            mailSender.MessageSent += message =>
            {
                if (message.ToString().Contains("HTTP Test") && message.ToString().Contains("Online"))
                {
                    messageSentTcs.SetResult(message);
                }
            };

            await WaitForConfigurationReplicationAsync();

            await messageSentTcs.Task.WaitAsync(_defaultTimeout);
        }

        [Test]
        public async Task MonitorShouldSendOfflineThenOnlineMailAsync()
        {
            var mailSender = Host1.Services.GetService<IEmailSender>() as TestEmailSender;

            // Wait for the first message to inform about the monitor being offline
            {
                var messageSentTcs = new TaskCompletionSource<MimeMessage>();

                mailSender.MessageSent += message =>
                {
                    if (message.ToString().Contains("HTTP Test"))
                    {
                        messageSentTcs.TrySetResult(message);
                    }
                };

                await WaitForConfigurationReplicationAsync();

                await messageSentTcs.Task.WaitAsync(_defaultTimeout);

                messageSentTcs.Task.Result.ToString().Should().Contain("Offline");
            }

            // Create monitor target web server
            using var testApp = WebApplication.Create();
            testApp.MapGet("/", () => "Hello world!");

            _ = Task.Run(() => testApp.Run("http://localhost:6000"));

            // Wait for the second message to inform about the monitor being online
            {
                var messageSentTcs = new TaskCompletionSource<MimeMessage>();

                mailSender.MessageSent += message =>
                {
                    if (message.ToString().Contains("HTTP Test"))
                    {
                        messageSentTcs.TrySetResult(message);
                    }
                };

                await messageSentTcs.Task.WaitAsync(_defaultTimeout);

                messageSentTcs.Task.Result.ToString().Should().Contain("Online");
            }
        }
    }
}
