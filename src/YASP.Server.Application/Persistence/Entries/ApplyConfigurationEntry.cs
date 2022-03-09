using YASP.Server.Application.Configuration;

namespace YASP.Server.Application.Persistence.Entries
{
    /// <summary>
    /// Raft log entry that makes a new app configuration valid for the cluster.
    /// </summary>
    public class ApplyConfigurationEntry : EntryBase
    {
        public AppConfiguration AppConfiguration { get; set; }
    }
}
