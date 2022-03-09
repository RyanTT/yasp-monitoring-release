namespace YASP.Server.Application.Notifications.Email
{
    /// <summary>
    /// Configuration for email notifications.
    /// </summary>
    [Serializable]
    public class EmailConfiguration
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string SenderAddress { get; set; }
        public string SenderDisplayName { get; set; }

        public Dictionary<string, string[]> Recipients { get; set; } = new Dictionary<string, string[]>();
    }
}
