using YASP.Server.Application.Notifications.Email;

namespace YASP.Server.Application.Notifications
{
    [Serializable]
    public class NotificationsConfiguration
    {
        public bool Enabled { get; set; } = true;
        public EmailConfiguration Email { get; set; } = new EmailConfiguration();
    }
}
