using HSMServer.SensorApi.Sessions.Files;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Core.Tests.SensorApi
{
    // The store of large files as the file connection sees it (#1884), in memory, with the rules of the
    // store of #1883 as its interface states them: one live lease per part, a bound on live leases, a part
    // known by its name, size, time and source, a repeat stored once, a finish that runs the connection's
    // checker. A test makes it refuse, fault or hold where it needs to. It survives a host, so a new host
    // over the same store is a restarted server.
    internal sealed class FakeFileUploadStore : IFileUploadStore
    {
        private readonly object _lock = new();
        private readonly Dictionary<Key, Entry> _entries = new();


        public long MaxFileBytes { get; set; } = 64L * 1024 * 1024 * 1024;

        public int MaxUploadsInProgress { get; set; } = 4;

        public HashSet<string> RefusedPaths { get; } = new();

        public volatile bool NoRoom;

        public volatile bool FaultWrites;

        public volatile bool FaultChecks;

        // Holds the next finish after it marked the file as being checked, until released.
        public Gate FinishGate { get; set; }

        // Holds the next append before it writes, until released.
        public Gate AppendGate { get; set; }

        public int LiveLeases
        {
            get
            {
                lock (_lock)
                    return _entries.Values.Count(entry => entry.LeaseId is not null);
            }
        }

        public int Appends;


        public Entry Find(string name)
        {
            lock (_lock)
                return _entries.Values.SingleOrDefault(entry => entry.Name == name);
        }

        public int Count
        {
            get
            {
                lock (_lock)
                    return _entries.Count;
            }
        }


        public Task<FileBeginResult> BeginAsync(FileBeginRequest request, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(request.Name) || request.Name.Length > 1024 || request.SourceIdentity?.Length > 256)
                throw new ArgumentException("broken contract");

            lock (_lock)
                return Task.FromResult(Begin(request));
        }

        private FileBeginResult Begin(FileBeginRequest request)
        {
            if (RefusedPaths.Contains(request.SensorPath))
                return Result(FileBeginOutcome.SensorRefused, error: $"No sensor is registered at '{request.SensorPath}'.");

            if (request.Size < 0 || request.Size > MaxFileBytes)
                return Result(FileBeginOutcome.TooLarge);

            if (request.PieceSize < 64 * 1024 || request.PieceSize > request.MaxPieceSize)
                return Result(FileBeginOutcome.BadPieceSize);

            var key = new Key(request.ProductId, request.SensorPath, request.Name, request.Size, request.SourceModifiedUtc, request.SourceIdentity);

            if (_entries.TryGetValue(key, out var entry))
            {
                switch (entry.State)
                {
                    case State.Verified:
                        return Result(FileBeginOutcome.AlreadyStored, entry);

                    case State.Checking:
                        return Result(FileBeginOutcome.Checking, entry, retryAfter: TimeSpan.FromSeconds(4.2));
                }

                if (entry.LeaseId is not null)
                    return Result(FileBeginOutcome.Busy, entry, retryAfter: TimeSpan.FromSeconds(30));

                if (entry.PieceSize != request.PieceSize)
                {
                    _entries.Remove(key);
                    entry = null;
                }
            }

            if (_entries.Values.Count(e => e.LeaseId is not null) >= MaxUploadsInProgress)
                return Result(FileBeginOutcome.TooManyUploads, retryAfter: TimeSpan.FromSeconds(60));

            if (entry is null)
            {
                if (NoRoom)
                    return Result(FileBeginOutcome.NoRoom, retryAfter: TimeSpan.FromMinutes(5));

                entry = new Entry(Guid.NewGuid(), key, request.PieceSize);
                _entries[key] = entry;
                entry.LeaseId = Guid.NewGuid();

                return Result(FileBeginOutcome.Started, entry);
            }

            entry.LeaseId = Guid.NewGuid();

            return Result(FileBeginOutcome.Continue, entry);
        }

        public async Task<FileAppendResult> AppendAsync(FileUploadLease lease, long pieceNumber, ReadOnlyMemory<byte> storedBytes, ReadOnlyMemory<byte> digest, CancellationToken ct)
        {
            Interlocked.Increment(ref Appends);

            if (AppendGate is { } gate)
            {
                AppendGate = null;
                await gate.PassAsync();
            }

            if (FaultWrites)
                throw new FileUploadStoreException("A write fault made by the test.");

            lock (_lock)
            {
                var entry = ById(lease.FileId);

                if (entry is null)
                    return new FileAppendResult(FileAppendOutcome.Discarded, 0);

                if (entry.LeaseId != lease.LeaseId || entry.State != State.Uploading)
                    return new FileAppendResult(FileAppendOutcome.LeaseLost, entry.Pieces.Count);

                var next = entry.Pieces.Count;

                if (pieceNumber < next)
                    return new FileAppendResult(FileAppendOutcome.AlreadyHave, next);

                if (pieceNumber > next)
                    return new FileAppendResult(FileAppendOutcome.OutOfOrder, next);

                if (pieceNumber >= entry.TotalPieces)
                    return new FileAppendResult(FileAppendOutcome.TooMany, next);

                entry.Pieces.Add((storedBytes.ToArray(), digest.ToArray()));

                return new FileAppendResult(FileAppendOutcome.Stored, entry.Pieces.Count);
            }
        }

        public Task DiscardAsync(FileUploadLease lease, string reason, CancellationToken ct)
        {
            lock (_lock)
            {
                if (ById(lease.FileId) is { } entry && entry.LeaseId == lease.LeaseId)
                {
                    _entries.Remove(entry.Key);
                    entry.DiscardReason = reason;
                }
            }

            return Task.CompletedTask;
        }

        public void Release(FileUploadLease lease)
        {
            lock (_lock)
            {
                if (ById(lease.FileId) is not { } entry || entry.LeaseId != lease.LeaseId || entry.State == State.Checking)
                    return;

                entry.LeaseId = null;

                // A part that received no piece goes with its lease (#1884 review, rule 7 of the coordinator).
                if (entry.State == State.Uploading && entry.Pieces.Count == 0 && entry.Size > 0)
                    _entries.Remove(entry.Key);
            }
        }

        public async Task<FileFinishResult> FinishAsync(FileUploadLease lease, byte[] wholeHash, IFilePartChecker checker, CancellationToken ct)
        {
            Entry entry;

            lock (_lock)
            {
                entry = ById(lease.FileId);

                if (entry is null || entry.LeaseId != lease.LeaseId)
                    return new FileFinishResult(FileFinishOutcome.LeaseLost, lease.FileId, null, 0, "The lease is lost; begin again.");

                if (entry.State == State.Checking)
                    return new FileFinishResult(FileFinishOutcome.Checking, lease.FileId, null, entry.Pieces.Count, null);

                if (entry.Pieces.Count < entry.TotalPieces)
                    return new FileFinishResult(FileFinishOutcome.Incomplete, lease.FileId, null, entry.Pieces.Count, null);

                entry.State = State.Checking;
            }

            var settled = false;

            try
            {
                if (FinishGate is { } gate)
                {
                    FinishGate = null;
                    await gate.PassAsync(ct);
                }

                if (FaultChecks)
                    throw new FileUploadStoreException("A read fault made by the test.");

                var check = await checker.CheckAsync(new Reader(entry), wholeHash, ct);

                lock (_lock)
                {
                    if (!check.Matches)
                    {
                        _entries.Remove(entry.Key);
                        entry.DiscardReason = check.Defect;
                        entry.State = State.Uploading;
                        settled = true;

                        return new FileFinishResult(FileFinishOutcome.Mismatch, entry.FileId, null, 0, "The stored file does not match the hash.");
                    }

                    entry.State = State.Verified;
                    entry.ContentHash = check.ContentHash;
                    entry.ValueTimeUtc = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
                    settled = true;

                    return new FileFinishResult(FileFinishOutcome.Verified, entry.FileId, entry.ValueTimeUtc, entry.TotalPieces, null);
                }
            }
            finally
            {
                if (!settled)
                    lock (_lock)
                        entry.State = State.Uploading;
            }
        }


        private Entry ById(Guid fileId) => _entries.Values.FirstOrDefault(entry => entry.FileId == fileId);

        private static FileBeginResult Result(FileBeginOutcome outcome, Entry entry = null, TimeSpan? retryAfter = null, string error = null) =>
            new(outcome, entry?.LeaseId is { } leaseId ? new FileUploadLease(entry.FileId, leaseId) : default, entry?.FileId ?? Guid.Empty,
                entry?.Pieces.Count ?? 0, entry?.TotalPieces ?? 0, entry?.PieceSize ?? 0, retryAfter, error);


        internal readonly record struct Key(Guid Product, string Path, string Name, long Size, DateTime Modified, string Source);


        internal enum State
        {
            Uploading,
            Checking,
            Verified,
        }


        internal sealed class Entry(Guid fileId, Key key, int pieceSize)
        {
            public Guid FileId { get; } = fileId;

            public Key Key { get; } = key;

            public string Name => Key.Name;

            public long Size => Key.Size;

            public int PieceSize { get; } = pieceSize;

            public long TotalPieces => (Size + PieceSize - 1) / PieceSize;

            public List<(byte[] Bytes, byte[] Digest)> Pieces { get; } = new();

            public Guid? LeaseId { get; set; }

            public State State { get; set; }

            public byte[] ContentHash { get; set; }

            public DateTime? ValueTimeUtc { get; set; }

            public string DiscardReason { get; set; }
        }


        private sealed class Reader(Entry entry) : IFilePartReader
        {
            public long Size => entry.Size;

            public int PieceSize => entry.PieceSize;

            public async IAsyncEnumerable<FileStoredPiece> ReadPiecesAsync([EnumeratorCancellation] CancellationToken ct)
            {
                for (var i = 0; i < entry.Pieces.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Yield();

                    yield return new FileStoredPiece(i, entry.Pieces[i].Bytes, entry.Pieces[i].Digest);
                }
            }
        }


        // A point a call stops at until the test lets it go.
        internal sealed class Gate
        {
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);


            public async Task PassAsync(CancellationToken ct = default)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
        }
    }
}
