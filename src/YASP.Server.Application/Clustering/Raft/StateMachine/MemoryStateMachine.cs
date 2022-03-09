using DotNext.IO;
using DotNext.IO.Log;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Runtime.CompilerServices;
using DotNext.Threading;

using YASP.Server.Application.Monitoring.Objects;
using YASP.Server.Application.Persistence.Entries;
using YASP.Server.Application.Persistence.Objects;

namespace YASP.Server.Application.Clustering.Raft.StateMachine
{
    public class MemoryStateMachine : IPersistentState, ILogCompactionSupport, IDisposable
    {
        private readonly AsyncReaderWriterLock _accessLock = new();
        private readonly AsyncManualResetEvent _commitEvent = new(false);

        private long _highestLogTerm, _highestLogIndex, _commitedLogIndex;

        public long LastCommittedEntryIndex => _commitedLogIndex.VolatileRead();
        public long LastUncommittedEntryIndex => _highestLogIndex.VolatileRead();

        public virtual Task InitializeAsync(CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        public async ValueTask AppendAsync<TEntryImpl>(ILogEntryProducer<TEntryImpl> entries, long startIndex, bool skipCommitted = false, CancellationToken token = default) where TEntryImpl : notnull, IRaftLogEntry
            => await InternalAppendManyAsync(entries, startIndex, skipCommitted, token);

        public async ValueTask<long> AppendAsync<TEntryImpl>(ILogEntryProducer<TEntryImpl> entries, CancellationToken token = default) where TEntryImpl : notnull, IRaftLogEntry
            => await InternalAppendManyAsync(entries, null, false, token);

        private async ValueTask<long> InternalAppendManyAsync<TEntryImpl>(ILogEntryProducer<TEntryImpl> entries, long? startIndex, bool skipCommitted = false, CancellationToken token = default) where TEntryImpl : notnull, IRaftLogEntry
        {
            using var lk = await _accessLock.AcquireWriteLockAsync(token).ConfigureAwait(false);

            startIndex ??= _highestLogIndex.VolatileRead() + 1L;

            long? resultIndex = null;

            while (entries.RemainingCount > 0)
            {
                token.ThrowIfCancellationRequested();

                await entries.MoveNextAsync().ConfigureAwait(false);

                if (!await InternalAppendAsync(entries.Current, startIndex.Value, skipCommitted, token).ConfigureAwait(false)) break;

                if (!resultIndex.HasValue) resultIndex = startIndex.Value;

                startIndex++;
            }

            return resultIndex ?? 0;
        }

        public async ValueTask AppendAsync<TEntryImpl>(TEntryImpl entry, long startIndex, CancellationToken token = default) where TEntryImpl : notnull, IRaftLogEntry
        {
            using var lk = await _accessLock.AcquireWriteLockAsync(token).ConfigureAwait(false);
            await InternalAppendAsync(entry, startIndex, false, token).ConfigureAwait(false);
        }

        private async ValueTask<bool> InternalAppendAsync<TEntryImpl>(TEntryImpl entry, long startIndex, bool skipCommitted = false, CancellationToken token = default) where TEntryImpl : notnull, IRaftLogEntry
        {
            if (startIndex <= _commitedLogIndex.VolatileRead())
            {
                if (!skipCommitted) throw new InvalidOperationException("Cannot append an entry in a committed range.");

                return true;
            }

            if (!entry.IsSnapshot && startIndex > _highestLogIndex.VolatileRead() + 1L) throw new ArgumentOutOfRangeException(nameof(startIndex));

            IRaftLogEntry processedEntry = default;

            // The type is already in the target datatype
            if (entry.GetType().Name == nameof(RaftJsonLogEntry))
            {
                processedEntry = entry;
            }
            else
            {
                var bytes = await entry.ToByteArrayAsync(token: token);

                // Skip empty entries
                if (bytes != null && bytes.Length > 0)
                {
                    // Parse the log entry
                    processedEntry = await ParseLogEntryAsync(entry, new BinaryReader(new MemoryStream(bytes)), token);
                }
                else
                {
                    processedEntry = entry;
                }
            }

            if (!await InternalAppendEntryAsync(processedEntry, startIndex, false, token).ConfigureAwait(false)) return false;

            if (entry.IsSnapshot)
            {
                _highestLogTerm.VolatileWrite(processedEntry.Term);
                _highestLogIndex.VolatileWrite(startIndex);
                _commitedLogIndex.VolatileWrite(startIndex);
                _commitEvent.Set(true);
            }
            else
            {
                _highestLogIndex.VolatileWrite(startIndex);
            }

            return true;
        }

        public ValueTask<long> CommitAsync(CancellationToken token = default) => InternalCommitAsync(null, token);
        public ValueTask<long> CommitAsync(long endIndex, CancellationToken token = default) => InternalCommitAsync(endIndex, token);

        private async ValueTask<long> InternalCommitAsync(long? endIndex, CancellationToken token)
        {
            using var lk = await _accessLock.AcquireWriteLockAsync(token).ConfigureAwait(false);

            var commited = _commitedLogIndex.VolatileRead();
            endIndex ??= _highestLogIndex.VolatileRead();

            // Did we already commit to this point?
            if (endIndex == commited) return 0;

            var startIndex = commited + 1L;

            (long amountCommitted, long term) = await InternalCommitManyEntriesAsync(startIndex, endIndex.Value, token);

            if (amountCommitted > 0)
            {
                _commitedLogIndex.VolatileWrite(startIndex + amountCommitted - 1L);
                _highestLogTerm.VolatileWrite(term);

                await ForceCompactionAsync(token);

                _commitEvent.Set(true);
            }

            return amountCommitted;
        }

        public async ValueTask<long> DropAsync(long startIndex, CancellationToken token = default)
        {
            using var lk = await _accessLock.AcquireWriteLockAsync(token).ConfigureAwait(false);

            if (startIndex <= _commitedLogIndex.VolatileRead()) throw new InvalidOperationException("Cannot append entry in a committed range.");

            var count = _highestLogIndex.VolatileRead() - startIndex + 1L;

            await DropEntriesAsync(startIndex, count, token);

            _highestLogIndex.VolatileWrite(startIndex - 1L);

            return count;
        }

        public ValueTask<TResult> ReadAsync<TResult>(ILogEntryConsumer<IRaftLogEntry, TResult> reader, long startIndex, CancellationToken token = default)
            => InternalReadAsync(reader, startIndex, null, token);

        public ValueTask<TResult> ReadAsync<TResult>(ILogEntryConsumer<IRaftLogEntry, TResult> reader, long startIndex, long endIndex, CancellationToken token = default)
            => InternalReadAsync(reader, startIndex, endIndex, token);

        private async ValueTask<TResult> InternalReadAsync<TResult>(ILogEntryConsumer<IRaftLogEntry, TResult> reader, long startIndex, long? endIndex, CancellationToken token = default)
        {
            using var lk = await _accessLock.AcquireReadLockAsync(token).ConfigureAwait(false);

            endIndex ??= _highestLogIndex.VolatileRead();

            if (endIndex > _highestLogIndex.VolatileRead()) throw new ArgumentOutOfRangeException(nameof(endIndex));

            var entries = new List<IRaftLogEntry>();

            long? snapshotIndex = null;

            for (var index = startIndex; index <= endIndex; index++)
            {
                (var readResult, var resultIndex) = await ReadEntryAsync<IRaftLogEntry>(index, token).ConfigureAwait(false);

                if (readResult != null)
                {
                    entries.Add(readResult);

                    if (readResult.IsSnapshot)
                    {
                        // Skip to the end of the snapshot for the next loop
                        index = resultIndex;

                        if (snapshotIndex != null)
                        {
                            throw new InvalidOperationException("Cannot have more than one snapshot.");
                        }

                        snapshotIndex = resultIndex;
                    }
                }
            }

            return await reader.ReadAsync<IRaftLogEntry, IRaftLogEntry[]>(entries.ToArray(), snapshotIndex, token).ConfigureAwait(false);
        }

        public ValueTask WaitForCommitAsync(CancellationToken token = default)
        {
            return _commitEvent.WaitAsync(token);
        }

        public async ValueTask WaitForCommitAsync(long index, CancellationToken token)
        {
            while (index > _commitedLogIndex.VolatileRead())
            {
                await _commitEvent.WaitAsync(token).ConfigureAwait(false);
            }
        }

        public async ValueTask ForceCompactionAsync(CancellationToken token)
        {
            var endIndex = LastCommittedEntryIndex;

            if (await ShouldCompactionRunAsync(endIndex, token)) await RunCompactionAsync(endIndex, token);
        }

        public async ValueTask EnsureConsistencyAsync(CancellationToken token = default)
        {
            while (_term.VolatileRead() != _highestLogTerm.VolatileRead())
            {
                await _commitEvent.WaitAsync(token).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _accessLock.Dispose();
            _commitEvent.Dispose();
        }

        private long _term;

        private volatile Shared<ClusterMemberId> _lastVote;

        public event Action<EntryBase> EntryAdded;

        public long Term => _term.VolatileRead();

        public bool IsVotedFor(in ClusterMemberId? id)
        {
            return _lastVote is null || id.HasValue && _lastVote.Value.Equals(id.GetValueOrDefault());
        }

        public ValueTask<long> IncrementTermAsync()
        {
            return new(_term.IncrementAndGet());
        }

        public ValueTask<long> IncrementTermAsync(ClusterMemberId member)
        {
            _lastVote = member;
            return new(_term.IncrementAndGet());
        }

        public ValueTask UpdateTermAsync(long term, bool resetLastVote)
        {
            _term.VolatileWrite(term);

            if (resetLastVote) _lastVote = null;

            return new();
        }

        public ValueTask UpdateVotedForAsync(ClusterMemberId? member)
        {
            _lastVote = member;

            return new();
        }

        protected Dictionary<long, IRaftLogEntry> Log { get; set; } = new();

        protected long? SnapshotIndex { get; set; }

        protected virtual Task<(IRaftLogEntry Entry, long Index)> CreateSnapshotAsync(IRaftLogEntry snapshotEntry, long? endIndex, CancellationToken token = default)
        {
            var snapshot = new ApplicationStateSnapshot();

            // Apply all log entries to the application state
            foreach (var logEntry in Log.Where(x => x.Key <= endIndex.Value).OrderBy(x => x.Key).ToList())
            {
                if (logEntry.Value.CommandId != 1337) continue;

                snapshot.Apply((logEntry.Value as RaftJsonLogEntry).Content);
            }

            // Cleanup the application state
            {
                // Iterate through all monitor state changes and determine which to keep and which to drop from the snapshot.
                // Any state changes younger than a 1 day old we keep in any case.
                // For any older state changes, we only keep the NEWEST state change that is also OLDER than 1 day.
                Dictionary<MonitorId, List<MonitorState>> statesToKeep = new(new MonitorId.EqualityComparer());

                foreach (var state in snapshot.MonitorStates.OrderBy(x => x.CheckTimestamp))
                {
                    if (!statesToKeep.ContainsKey(state.MonitorId))
                    {
                        statesToKeep.Add(state.MonitorId, new List<MonitorState> { state });

                        continue;
                    }

                    var existingStates = statesToKeep[state.MonitorId];
                    var previousState = existingStates.Last();

                    // If we are > 1 day old, save only the "newest > 1 day old" entry
                    if (state.CheckTimestamp < DateTimeOffset.UtcNow.AddDays(-1))
                    {
                        existingStates.Clear();
                        existingStates.Add(state);

                        continue;
                    }

                    if (previousState.MonitorStatus != state.MonitorStatus)
                    {
                        existingStates.Add(state);
                    }
                }


                snapshot.MonitorStates.RemoveAll(x => !(statesToKeep.ContainsKey(x.MonitorId) && statesToKeep[x.MonitorId].Contains(x)));

                // Clean up all notifications that are associated with status updates that have been deleted
                snapshot.NotificationsSent.RemoveAll(x => !snapshot.MonitorStates.Any(state => state.MonitorId == x.MonitorId && state.CheckTimestamp == x.CheckTimestamp));

                // Clean up all monitor node assignments that we no longer need.
                // No longer needed => Older than the oldest MonitorState CheckTimestamp - 1 Entry
                var lastState = snapshot.MonitorStates.OrderBy(x => x.CheckTimestamp).FirstOrDefault();

                var removableAssignments = snapshot.MonitorNodesAssignments
                    .Where(x => lastState == null ? false : x.Timestamp < lastState.CheckTimestamp)
                    .SkipLast(1)
                    .ToList();

                snapshot.MonitorNodesAssignments.RemoveAll(x => removableAssignments.Contains(x));
            }

            snapshotEntry = new RaftJsonLogEntry(snapshot, Term)
            {
                IsSnapshot = true,
                Timestamp = DateTimeOffset.UtcNow
            };

            return Task.FromResult((snapshotEntry, endIndex ?? 0));
        }

        protected async ValueTask<bool> InternalAppendEntryAsync<TEntryImpl>(TEntryImpl entry, long index, bool skipCommitted = false, CancellationToken token = default) where TEntryImpl : notnull, IRaftLogEntry
        {
            token.ThrowIfCancellationRequested();

            if (entry.IsSnapshot)
            {
                Log.Clear();
                SnapshotIndex = index;

                if (entry is RaftJsonLogEntry jsonEntry)
                {
                    EntryAdded?.Invoke(jsonEntry.Content);
                }
            }

            Log[index] = entry;

            return true;
        }

        protected async ValueTask<(long, long)> InternalCommitManyEntriesAsync(long startIndex, long endIndex, CancellationToken token = default)
        {
            long commitedEntries = 0;
            long lastTerm = 0;

            for (var index = startIndex; index <= endIndex; index++)
            {
                token.ThrowIfCancellationRequested();

                if (Log.TryGetValue(index, out var entry) && await CommitEntryAsync(entry, index, token))
                {
                    lastTerm = entry.Term;
                    commitedEntries++;
                }
                else
                {
                    break;
                }
            }

            return (commitedEntries, lastTerm);
        }

        protected async Task<bool> CommitEntryAsync(IRaftLogEntry entry, long index, CancellationToken token = default)
        {
            if (entry is not RaftJsonLogEntry jsonEntry) return true;

            EntryAdded?.Invoke(jsonEntry.Content);

            return true;
        }

        public async ValueTask DropEntriesAsync(long startIndex, long count, CancellationToken token = default)
        {
            for (var i = 0L; i < count; i++)
            {
                token.ThrowIfCancellationRequested();

                Log.Remove(startIndex + i);
            }
        }

        protected ValueTask<(TResult Result, long ResultIndex)> ReadEntryAsync<TResult>(long index, CancellationToken token = default) where TResult : IRaftLogEntry
        {
            if (SnapshotIndex.HasValue) index = Math.Max(index, SnapshotIndex.Value);

            if (Log.TryGetValue(index, out var value))
            {
                return ValueTask.FromResult(((TResult)value, index));
            }

            return default;
        }

        public ValueTask<bool> ShouldCompactionRunAsync(long endIndex, CancellationToken token = default)
        {
            return ValueTask.FromResult(Log.Count > 100);
        }

        public async ValueTask RunCompactionAsync(long endIndex, CancellationToken token = default)
        {
            var snapshot = await CreateSnapshotAsync(null!, endIndex, token);

            if (snapshot != default && snapshot.Entry != null)
            {
                Log[endIndex] = snapshot.Entry;
            }

            SnapshotIndex = endIndex;

            Log.Keys
               .Where(x => x < endIndex)
               .ToList()
               .ForEach(x => Log.Remove(x));
        }

        protected ValueTask<IRaftLogEntry> ParseLogEntryAsync<TEntryImpl>(TEntryImpl entry, BinaryReader reader, CancellationToken token = default) where TEntryImpl : IRaftLogEntry
        {
            if (entry.IsSnapshot || entry.CommandId == 1337)
            {
                var jsonEntry = new RaftJsonLogEntry(entry);

                jsonEntry.Deserialize(reader);

                return ValueTask.FromResult<IRaftLogEntry>(jsonEntry);
            }

            return ValueTask.FromResult<IRaftLogEntry>(entry);
        }
    }
}
