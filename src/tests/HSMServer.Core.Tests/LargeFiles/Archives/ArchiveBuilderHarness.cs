using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.LargeFiles.Archives;
using NLog;
using NLog.Targets;

namespace HSMServer.Core.Tests.LargeFiles.Archives
{
    /// <summary>A builder over the fake store and passwords, a settable clock and a log of its own (not the global NLog).</summary>
    internal sealed class ArchiveBuilderHarness : IDisposable
    {
        private readonly MemoryTarget _log = new() { Layout = "${level}|${message}|${exception:format=tostring}" };


        public DateTime Now { get; set; } = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

        public FakeLargeFileArchiveStore Store { get; } = new();

        public FakeDumpPasswordService Passwords { get; } = new();

        public HookedWriter Writer { get; } = new();

        public ArchiveBuilder Builder { get; }

        public List<(Guid FileId, bool Ok, string Error)> Finished { get; } = new();

        public Guid Product { get; } = Guid.NewGuid();

        public IReadOnlyList<string> LogLines => _log.Logs.ToList();


        public ArchiveBuilderHarness(ArchiveBuilderOptions options = null)
        {
            Passwords.UtcNow = () => Now;

            var factory = new LogFactory();
            factory.Setup().LoadConfiguration(c => c.ForLogger().WriteTo(_log));

            Builder = new ArchiveBuilder(Store, Writer, Passwords, () => Now, options, factory.GetLogger(nameof(ArchiveBuilderHarness)));
            Builder.BuildFinished += (id, ok, error) =>
            {
                lock (Finished)
                    Finished.Add((id, ok, error));
            };
        }


        public Task Run() => Builder.RunDueAsync(CancellationToken.None);

        public void Advance(TimeSpan by) => Now += by;

        /// <summary>The content of the file's archive, opened with the password.</summary>
        public byte[] ReadArchive(Guid fileId, string password)
        {
            var record = Store.TryGetFile(fileId);
            using var stream = new FileStream(Store.ArchivePath(record), FileMode.Open, FileAccess.Read, FileShare.Read);
            using var entry = new SharpZipArchiveWriter().OpenEntry(stream, password);

            return SharpZipArchiveWriterTests.ReadAll(entry.Content);
        }

        public static bool SameHash(byte[] content, byte[] sha256) => SHA256.HashData(content).AsSpan().SequenceEqual(sha256);

        public void Dispose()
        {
            Builder.Dispose();
            Store.Dispose();
        }
    }


    /// <summary>The real writer, with hooks around its calls and a count of the writes running at once.</summary>
    internal sealed class HookedWriter : IArchiveWriter
    {
        private readonly SharpZipArchiveWriter _inner = new();
        private int _running;


        public Action<string> BeforeWrite { get; set; }

        public Func<string, Exception> FailWrite { get; set; }

        public int MaxConcurrentWrites { get; private set; }

        public int Writes { get; private set; }


        public void Write(Stream output, string entryName, DateTime entryTimeUtc, long size, Stream content, string password, int deflateLevel, CancellationToken ct)
        {
            var running = Interlocked.Increment(ref _running);
            try
            {
                lock (this)
                {
                    Writes++;
                    MaxConcurrentWrites = Math.Max(MaxConcurrentWrites, running);
                }

                BeforeWrite?.Invoke(password);

                if (FailWrite?.Invoke(password) is { } fault)
                    throw fault;

                _inner.Write(output, entryName, entryTimeUtc, size, content, password, deflateLevel, ct);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        public ArchiveEntryReader OpenEntry(Stream archive, string password) => _inner.OpenEntry(archive, password);
    }
}
