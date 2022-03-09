using DotNext.IO;
using DotNext.Net.Cluster.Consensus.Raft;

using System.Text.Json;

using YASP.Server.Application.Persistence.Entries;

namespace YASP.Server.Application.Clustering.Raft
{
    /// <summary>
    /// dotNext helper class to serialize content into a custom format that we can read again the same way.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class RaftJsonLogEntry : IRaftLogEntry
    {
        public RaftJsonLogEntry(EntryBase content, long term)
        {
            Content = content;
            Term = term;
            Timestamp = DateTimeOffset.UtcNow;
        }

        public RaftJsonLogEntry(IRaftLogEntry raftLogEntry)
        {
            Term = raftLogEntry.Term;
            Timestamp = raftLogEntry.Timestamp;
            IsSnapshot = raftLogEntry.IsSnapshot;
        }

        public bool IsSnapshot { get; set; }
        public long Term { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public int? CommandId => 1337;

        public bool IsReusable => true;

        public long? Length => null;

        public EntryBase Content { get; set; }

        public async ValueTask WriteToAsync<TWriter>(TWriter writer, CancellationToken token) where TWriter : IAsyncBinaryWriter
        {
            var memory = new MemoryStream();

            var binaryWriter = new BinaryWriter(memory);

            var wrapper = new EntryTypeWrapper
            {
                Type = Content.GetType().AssemblyQualifiedName,
                Content = JsonSerializer.Serialize<object>(Content)
            };

            binaryWriter.Write(JsonSerializer.Serialize(wrapper));

            await writer.WriteAsync(memory.GetBuffer(), token: token);
        }

        public void Deserialize(BinaryReader reader)
        {
            var str = reader.ReadString();

            // Read wrapper json.
            var requestWrapper = JsonSerializer.Deserialize<EntryTypeWrapper>(str);

            // Get the body type of the wrapper.
            var requestContentType = Type.GetType(requestWrapper.Type);

            // Check the type is safe to deserialise into (we don't want to instantiate odd classes)
            if (!requestContentType.IsAssignableTo(typeof(EntryBase)))
            {
                throw new InvalidOperationException($"Content type does not inherit from {typeof(EntryBase).Name}.");
            }

            // Deserialize
            var content = JsonSerializer.Deserialize(requestWrapper.Content, Type.GetType(requestWrapper.Type));

            Content = content as EntryBase;
        }

        public class EntryTypeWrapper
        {
            public string Type { get; set; }
            public string Content { get; set; }
        }
    }
}
