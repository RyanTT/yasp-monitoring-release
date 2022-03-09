//using DotNext.Net.Cluster.Consensus.Raft;

//using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.Options;

//using Nito.AsyncEx;

//using System.Collections.Concurrent;
//using System.Text;
//using System.Text.Json;

//using YASP.Server.Application.Monitoring.Objects;
//using YASP.Server.Application.Options;
//using YASP.Server.Application.Persistence.Entries;
//using YASP.Server.Application.Persistence.Objects;

//namespace YASP.Server.Application.Clustering.Raft
//{
//    /// <summary>
//    /// Simple state machine that dispatches committed log entries via the <see cref="EntryAdded"/> event.
//    /// </summary>
//    public class RaftMemoryBasedStateMachine : MemoryBasedStateMachine
//    {
//        /// <summary>
//        /// Event that gets fired once a log entry is committed. Will fire for each log entry in order on startup.
//        /// </summary>
//        public event Action<EntryBase> EntryAdded;

//        internal readonly ILogger<RaftMemoryBasedStateMachine> _logger;
//        private readonly IOptions<RootOptions> _options;

//        private ConcurrentDictionary<long, LogEntry> _pendingEntries = new();
//        private ConcurrentDictionary<long, (Task Task, CancellationTokenSource CTS)> _applyTasks = new();
//        private AsyncLock _applyLock = new();

//        private Task _waitForCommitTask;
//        private CancellationTokenSource _waitForCommitTaskCancel = new CancellationTokenSource();
//        private long _waitForCommitTaskIndex;

//        public RaftMemoryBasedStateMachine(ILogger<RaftMemoryBasedStateMachine> logger, IOptions<RootOptions> options)
//            : base($"./data/raft/{new Uri(options.Value.Cluster.ListenEndpoint).Host}_{new Uri(options.Value.Cluster.ListenEndpoint).Port}/log", 100, new Options
//            {
//                CompactionMode = CompactionMode.Background
//            })
//        {
//            _logger = logger;
//            _options = options;
//        }

//        protected override async ValueTask ApplyAsync(LogEntry raftEntry)
//        {
//            _logger.LogDebug($"RaftMemoryBasedStateMachine::ApplyAsync: Called for index {raftEntry.Index}, raftEntry.Type = {raftEntry.GetType().Name}");

//            using var lk = await _applyLock.LockAsync();

//            _applyTasks.Where(x => x.Key >= raftEntry.Index).ToList().ForEach(x =>
//            {
//                x.Value.CTS.Cancel();

//                _applyTasks.Remove(x.Key, out _);
//            });

//            var processTaskCancelToken = new CancellationTokenSource();
//            var processTask = Task.Run(async () =>
//            {
//                _logger.LogDebug($"RaftMemoryBasedStateMachine::ApplyAsync: ProcessTask: Waiting for commit of index {raftEntry.Index}");

//                // Wait for the index to be committed.
//                await WaitForCommitAsync(raftEntry.Index, processTaskCancelToken.Token);

//                _logger.LogDebug($"RaftMemoryBasedStateMachine::ApplyAsync: ProcessTask: Index {raftEntry.Index} was committed, processing it locally");

//                // Deserialize the log entry data.
//                EntryBase value = default;

//                var reader = raftEntry.GetReader();

//                var typeString = await reader.ReadStringAsync(DotNext.IO.LengthFormat.PlainLittleEndian, new DotNext.Text.DecodingContext(Encoding.UTF8, true));
//                var contentString = await reader.ReadStringAsync(DotNext.IO.LengthFormat.PlainLittleEndian, new DotNext.Text.DecodingContext(Encoding.UTF8, true));

//                value = JsonSerializer.Deserialize(contentString, Type.GetType(typeString)) as EntryBase;

//                _logger.LogDebug($"RaftMemoryBasedStateMachine::ApplyAsync: ProcessTask: Dispatching entry index {raftEntry.Index} into the application, valueType = {value.GetType().Name}");

//                // Invoke our event into the rest of the application.
//                EntryAdded?.Invoke(value);
//            });

//            _applyTasks.TryAdd(raftEntry.Index, (processTask, processTaskCancelToken));

//            //// Add to our list of entries we need to process once the time comes they are committed.
//            //// We may also receive logs for indexes we already have, so we need to overwrite them (this is intended behaviour by Raft)
//            //_pendingEntries.AddOrUpdate(raftEntry.Index, index => raftEntry, (index, existingEntry) => raftEntry);

//            //// Only start a new task to process committed entries if this entrys index is smaller than the one the task was dispatched for or if there is no task.
//            //if (raftEntry.Index <= _waitForCommitTaskIndex || _waitForCommitTask == null || _waitForCommitTask.IsCompleted)
//            //{
//            //    if (_waitForCommitTask != null)
//            //    {
//            //        _waitForCommitTaskCancel?.Cancel();
//            //    }

//            //    _waitForCommitTaskCancel = new CancellationTokenSource();

//            //    _waitForCommitTask = Task.Run(async () =>
//            //    {
//            //        while (_pendingEntries.Count > 0)
//            //        {
//            //            long lowestIndex = default;

//            //            try
//            //            {
//            //                lowestIndex = _pendingEntries.Keys.OrderBy(x => x).First();

//            //                // Wait for the index to be committed.
//            //                await WaitForCommitAsync(lowestIndex, _waitForCommitTaskCancel.Token);
//            //                _pendingEntries.TryGetValue(lowestIndex, out var entry);

//            //                // Deserialize the log entry data.
//            //                EntryBase value = default;

//            //                var reader = entry.GetReader();

//            //                var typeString = await reader.ReadStringAsync(DotNext.IO.LengthFormat.PlainLittleEndian, new DotNext.Text.DecodingContext(Encoding.UTF8, true));
//            //                var contentString = await reader.ReadStringAsync(DotNext.IO.LengthFormat.PlainLittleEndian, new DotNext.Text.DecodingContext(Encoding.UTF8, true));

//            //                value = JsonSerializer.Deserialize(contentString, Type.GetType(typeString)) as EntryBase;

//            //                // Invoke our event into the rest of the application.
//            //                EntryAdded?.Invoke(value);

//            //                // Remove from dictionary
//            //                _pendingEntries.Remove(lowestIndex, out _);

//            //                // Set new lowest index that we are waiting for
//            //                if (_pendingEntries.Count > 0)
//            //                {
//            //                    _waitForCommitTaskIndex = _pendingEntries.Keys.OrderBy(x => x).First();
//            //                }
//            //            }
//            //            catch (Exception ex)
//            //            {
//            //                _logger.LogDebug(ex, $"Cannot process committed entry.");

//            //                // Not sure why, but sometime this fails and it doesn't seem to affect anything when ignoring the entry!
//            //                // Maybe a bug in the library?
//            //                if (_pendingEntries.ContainsKey(lowestIndex)) _pendingEntries.Remove(lowestIndex, out _);

//            //                // Set new lowest index that we are waiting for
//            //                if (_pendingEntries.Count > 0)
//            //                {
//            //                    _waitForCommitTaskIndex = _pendingEntries.Keys.OrderBy(x => x).First();
//            //                }
//            //            }
//            //        }
//            //    });
//            //}
//        }

//        protected override SnapshotBuilder CreateSnapshotBuilder(in SnapshotBuilderContext context)
//        {
//            return new CustomSnapshotBuilder(context, this);
//        }

//        /// <summary>
//        /// Custom snapshot builder that.. builds a snapshot of the log.
//        /// </summary>
//        protected class CustomSnapshotBuilder : IncrementalSnapshotBuilder
//        {
//            private readonly RaftMemoryBasedStateMachine _stateMachine;

//            public ApplicationStateSnapshot Snapshot { get; private set; }

//            public CustomSnapshotBuilder(in SnapshotBuilderContext context, RaftMemoryBasedStateMachine stateMachine) : base(context)
//            {
//                Snapshot = new ApplicationStateSnapshot();
//                _stateMachine = stateMachine;
//            }

//            /// <summary>
//            /// Serializes the snapshot we built into a log entry.
//            /// </summary>
//            /// <typeparam name="TWriter"></typeparam>
//            /// <param name="writer"></param>
//            /// <param name="token"></param>
//            /// <returns></returns>
//            public override async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token)
//            {
//                try
//                {
//                    var json = JsonSerializer.Serialize(Snapshot);

//                    await writer.WriteStringAsync(typeof(ApplicationStateSnapshot).AssemblyQualifiedName.AsMemory(), new DotNext.Text.EncodingContext(Encoding.UTF8, true), DotNext.IO.LengthFormat.PlainLittleEndian, token);
//                    await writer.WriteStringAsync(json.AsMemory(), new DotNext.Text.EncodingContext(Encoding.UTF8, true), DotNext.IO.LengthFormat.PlainLittleEndian, token);
//                }
//                catch (Exception ex)
//                {
//                    _stateMachine._logger.LogError(ex, $"Failed to write snapshot.");
//                }
//            }

//            protected override async ValueTask ApplyAsync(LogEntry raftEntry)
//            {
//                try
//                {
//                    // Deserialize the log entry.
//                    EntryBase value = default;

//                    var reader = raftEntry.GetReader();

//                    var typeString = await reader.ReadStringAsync(DotNext.IO.LengthFormat.PlainLittleEndian, new DotNext.Text.DecodingContext(Encoding.UTF8, true));
//                    var contentString = await reader.ReadStringAsync(DotNext.IO.LengthFormat.PlainLittleEndian, new DotNext.Text.DecodingContext(Encoding.UTF8, true));

//                    value = JsonSerializer.Deserialize(contentString, Type.GetType(typeString)) as EntryBase;

//                    Snapshot.Apply(value);

//                    // Clean up states where we can. We only need minimum the last state to operate,
//                    // but saving the last day might seem nicer on the status page if we want to display it.
//                    Dictionary<MonitorId, List<MonitorState>> statesToKeep = new(new MonitorId.EqualityComparer());

//                    foreach (var state in Snapshot.MonitorStates.OrderBy(x => x.CheckTimestamp))
//                    {
//                        if (!statesToKeep.ContainsKey(state.MonitorId))
//                        {
//                            statesToKeep.Add(state.MonitorId, new List<MonitorState> { state });

//                            continue;
//                        }

//                        var existingStates = statesToKeep[state.MonitorId];
//                        var previousState = existingStates.Last();

//                        // If we are > 1 day old, save only the "newest > 1 day old" entry
//                        if (state.CheckTimestamp < DateTimeOffset.UtcNow.AddDays(-1))
//                        {
//                            existingStates.Clear();
//                            existingStates.Add(state);

//                            continue;
//                        }

//                        if (previousState.MonitorStatus != state.MonitorStatus)
//                        {
//                            existingStates.Add(state);
//                        }
//                    }


//                    Snapshot.MonitorStates.RemoveAll(x => !(statesToKeep.ContainsKey(x.MonitorId) && statesToKeep[x.MonitorId].Contains(x)));

//                    // Clean up all notifications that are associated with status updates that have been deleted
//                    Snapshot.NotificationsSent.RemoveAll(x => !Snapshot.MonitorStates.Any(state => state.MonitorId == x.MonitorId && state.CheckTimestamp == x.CheckTimestamp));

//                    // Clean up all monitor node assignments that we no longer need.
//                    // No longer needed => Older than the oldest MonitorState CheckTimestamp - 1 Entry
//                    var lastState = Snapshot.MonitorStates.OrderBy(x => x.CheckTimestamp).FirstOrDefault();

//                    var removableAssignments = Snapshot.MonitorNodesAssignments
//                        .Where(x => lastState == null ? false : x.Timestamp < lastState.CheckTimestamp)
//                        .SkipLast(1)
//                        .ToList();

//                    Snapshot.MonitorNodesAssignments.RemoveAll(x => removableAssignments.Contains(x));
//                }
//                catch (Exception ex)
//                {
//                    _stateMachine._logger.LogDebug(ex, $"Failed to apply entry to snapshot builder.");
//                }
//            }
//        }
//    }
//}
