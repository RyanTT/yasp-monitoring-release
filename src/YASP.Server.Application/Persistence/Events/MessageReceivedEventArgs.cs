using YASP.Server.Application.Persistence.Entries;

namespace YASP.Server.Application.Persistence.Events
{
    public class MessageReceivedEventArgs
    {
        public EntryBase Entry { get; set; }
    }
}
