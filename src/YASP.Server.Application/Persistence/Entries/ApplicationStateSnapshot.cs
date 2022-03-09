using YASP.Server.Application.Configuration;
using YASP.Server.Application.Persistence.Objects;

namespace YASP.Server.Application.Persistence.Entries
{
    /// <summary>
    /// Represents the current application state.
    /// </summary>
    public class ApplicationStateSnapshot : EntryBase
    {
        /// <summary>
        /// All monitor status changes.
        /// </summary>
        public List<MonitorState> MonitorStates { get; set; } = new List<MonitorState>();

        /// <summary>
        /// All monitor assignments to nodes.
        /// </summary>
        public List<MonitorNodesAssignment> MonitorNodesAssignments { get; set; } = new List<MonitorNodesAssignment>();

        /// <summary>
        /// All notifications that were sent.
        /// </summary>
        public List<NotificationSent> NotificationsSent { get; set; } = new List<NotificationSent>();

        /// <summary>
        /// The currently active app configuration.
        /// </summary>
        public AppConfiguration AppConfiguration { get; set; } = new AppConfiguration();

        /// <summary>
        /// Applies an entry of the Raft log to this snapshot and thus modifies it.
        /// </summary>
        /// <param name="value"></param>
        public void Apply(EntryBase value)
        {
            switch (value)
            {
                case ApplicationStateSnapshot entry:
                    {
                        AppConfiguration = entry.AppConfiguration;
                        MonitorStates = entry.MonitorStates;
                        MonitorNodesAssignments = entry.MonitorNodesAssignments;
                        NotificationsSent = entry.NotificationsSent;

                        break;
                    }

                case ApplyConfigurationEntry entry:
                    {
                        AppConfiguration = entry.AppConfiguration;

                        break;
                    }

                case ApplyMonitorStateChangeEntry entry:
                    {
                        MonitorStates.Add(new MonitorState
                        {
                            MonitorId = entry.MonitorId,
                            CheckTimestamp = entry.CheckTimestamp,
                            MonitorStatus = entry.Status,
                            IsRedundantEntry = entry.IsRedundantEntry
                        });

                        break;
                    }

                case ApplyMonitorNodeAssignmentEntry entry:
                    {
                        MonitorNodesAssignments.Add(new MonitorNodesAssignment
                        {
                            MonitorId = entry.MonitorId,
                            Nodes = entry.Nodes,
                            Timestamp = entry.Timestamp,
                            MonitorConfiguration = entry.MonitorConfiguration
                        });

                        break;
                    }

                case ApplyNotificationSentEntry entry:
                    {
                        NotificationsSent.Add(new NotificationSent
                        {
                            MonitorId = entry.MonitorId,
                            CheckTimestamp = entry.CheckTimestamp,
                            NotificationProviderIdentifier = entry.NotificationIdentifier
                        });

                        break;
                    }
            }
        }

        /// <summary>
        /// Creates a safe copy to read from.
        /// </summary>
        /// <returns></returns>
        public ApplicationStateSnapshot CreateCopy()
        {
            return new ApplicationStateSnapshot
            {
                MonitorStates = MonitorStates.Select(x => new MonitorState
                {
                    MonitorId = x.MonitorId,
                    CheckTimestamp = x.CheckTimestamp,
                    MonitorStatus = x.MonitorStatus,
                    IsRedundantEntry = x.IsRedundantEntry
                }).ToList(),
                AppConfiguration = new AppConfiguration
                {
                    Monitors = AppConfiguration.Monitors.ToList(),
                    Revision = AppConfiguration.Revision,
                    Notifications = AppConfiguration.Notifications,
                    Pages = AppConfiguration.Pages.ToList()
                },
                MonitorNodesAssignments = MonitorNodesAssignments.ToList(),
                NotificationsSent = NotificationsSent.ToList(),
            };
        }
    }
}
