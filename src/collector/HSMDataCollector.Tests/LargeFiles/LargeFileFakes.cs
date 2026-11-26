#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client.Session;
using HSMDataCollector.LargeFiles;
using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Tests.LargeFiles
{
    /// <summary>The seeded content both conformance drivers reproduce (#1888 design §12.10): xorshift32, the low byte of each state.</summary>
    public static class SeededContent
    {
        public static byte[] Make(long size, uint seed)
        {
            var bytes = new byte[size];
            var x = seed == 0 ? 1u : seed;

            for (long i = 0; i < size; i++)
            {
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                bytes[i] = (byte)x;
            }

            return bytes;
        }
    }


    /// <summary>
    /// Time the test decides: the clock moves only when a retry, a throttle or the test moves it; scan waits are short real
    /// waits, so the uploader polls while the test changes files and the clock.
    /// </summary>
    internal sealed class VirtualTimings : LargeFileTimings
    {
        private long _ticks = TimeSpan.FromHours(1).Ticks;

        internal ConcurrentQueue<(WaitKind Kind, TimeSpan Span)> Waits { get; } = new ConcurrentQueue<(WaitKind, TimeSpan)>();

        private long _scans;

        /// <summary>Times the uploader has looked at its folders and queue.</summary>
        internal long Scans => Interlocked.Read(ref _scans);

        internal override void Scanned() => Interlocked.Increment(ref _scans);

        /// <summary>
        /// One second of the test's clock, then waits until the uploader has looked at the world after it (or 2 s pass: the
        /// uploader is in an upload, which a step does not wait for). So a test never outruns the scans it relies on.
        /// </summary>
        internal void Step(Action wake)
        {
            var before = Scans;

            Advance(TimeSpan.FromSeconds(1));
            wake?.Invoke();

            var until = DateTime.UtcNow.AddSeconds(2);

            while (Scans <= before && DateTime.UtcNow < until)
                Thread.Sleep(2);
        }

        internal override TimeSpan Now => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

        internal void Advance(TimeSpan span) => Interlocked.Add(ref _ticks, span.Ticks);

        internal TimeSpan Scan { get; set; } = TimeSpan.FromMilliseconds(250);

        internal override TimeSpan ScanInterval(TimeSpan settle) => Scan;

        internal override TimeSpan StateLockWait => TimeSpan.FromMilliseconds(200);

        internal override TimeSpan StopWait => TimeSpan.FromSeconds(2);

        internal TimeSpan Idle { get; set; } = TimeSpan.FromMinutes(30);

        internal override TimeSpan IdleExit => Idle;

        /// <summary>What the uploader reads of an open file; null leaves it as it is.</summary>
        internal Func<string, FileFacts, FileFacts> FactsHook { get; set; }

        internal override FileFacts Observe(string path, FileFacts facts) => FactsHook == null ? facts : FactsHook(path, facts);

        /// <summary>When set, a retry wait blocks (really) until the test sets this event: a test can look at the state in a wait.</summary>
        internal ManualResetEventSlim HoldRetries { get; set; }

        internal override bool Wait(TimeSpan span, WaitKind kind, WaitHandle wake, CancellationToken token)
        {
            // A wait of the uploader is a moment it has looked at the world: a step of the test need not wait longer.
            Interlocked.Increment(ref _scans);

            if (kind == WaitKind.Scan)
            {
                var ms = (int)Math.Min(20, Math.Max(1, span.TotalMilliseconds));
                WaitHandle.WaitAny(new[] { wake, token.WaitHandle }, ms);
                return false;
            }

            Waits.Enqueue((kind, span));

            if (kind == WaitKind.Retry && HoldRetries != null)
            {
                try { HoldRetries.Wait(token); }
                catch (OperationCanceledException) { return false; }
            }

            if (token.IsCancellationRequested)
                return false;

            Advance(span);
            return true;
        }
    }


    /// <summary>The file connection in memory, over a <see cref="FakeFileStore"/>.</summary>
    internal sealed class FakeFileChannel : IFileChannel
    {
        private readonly FakeFileStore _store;
        private long _nextId;
        private volatile bool _open;

        internal FakeFileChannel(FakeFileStore store) => _store = store;

        internal int Opens;
        internal int Closes;
        internal int Aborts;

        /// <summary>Upgrade statuses to refuse the next opens with (0 = accept).</summary>
        internal ConcurrentQueue<int> RefuseOpens { get; } = new ConcurrentQueue<int>();

        /// <summary>Called on the uploader's thread before every piece is answered: a test may change the file there.</summary>
        internal Action<long> BeforePieceAnswer { get; set; }

        /// <summary>Called before every request is answered.</summary>
        internal Action<string> BeforeRequest { get; set; }

        /// <summary>Blocks every piece until set; a test can stop or dispose in the middle.</summary>
        internal ManualResetEventSlim HoldPieces { get; set; }

        internal ManualResetEventSlim PieceArrived { get; } = new ManualResetEventSlim(false);

        public bool IsOpen => _open;

        public TimeSpan LastPieceWrite { get; set; }

        public Task<FileOpen> OpenAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Opens);

            if (RefuseOpens.TryDequeue(out var status) && status != 0)
            {
                _open = false;
                return Task.FromResult(new FileOpen(null, status, $"the upgrade was refused with {status}"));
            }

            _open = true;
            _store.Note("open");

            return Task.FromResult(new FileOpen(new FileHello(_store.MaxFrameBytes, _store.HeartbeatSeconds, _store.MaxFileBytes, _store.PieceBytes), 0, null));
        }

        public Task<FileReply> RequestAsync(string op, string body, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (!_open)
                return Task.FromResult(FileReply.Lost("no file connection is open"));

            BeforeRequest?.Invoke(op);

            var id = Interlocked.Increment(ref _nextId);

            return Task.FromResult(ToReply(_store.Request(id, System.Text.Encoding.UTF8.GetString(SessionProtocol.BuildRequest(id, op, body)))));
        }

        public Task<FileReply> SendPieceAsync(byte[] frame, int length, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (!_open)
                return Task.FromResult(FileReply.Lost("no file connection is open"));

            var id = Interlocked.Increment(ref _nextId);
            var copy = frame.Take(length).ToArray();

            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(copy.AsSpan(8), id);
            PieceArrived.Set();

            if (HoldPieces != null)
            {
                try { HoldPieces.Wait(token); }
                catch (OperationCanceledException) { throw; }
            }

            token.ThrowIfCancellationRequested();

            if (!_open)
                return Task.FromResult(FileReply.Lost("the connection was aborted"));

            BeforePieceAnswer?.Invoke(System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(copy.AsSpan(32)));

            return Task.FromResult(ToReply(_store.Piece(copy)));
        }

        private FileReply ToReply(FileReplyText text)
        {
            switch (text.Kind)
            {
                case FileAnswer.AnswerKind.Status:
                    SessionFrame.TryParse(text.Json, out var frame);
                    return FileReply.Ack(frame);

                case FileAnswer.AnswerKind.Close:
                    _open = false;
                    return FileReply.Lost($"closed by the server ({text.CloseCode})", text.CloseCode);

                case FileAnswer.AnswerKind.Drop:
                    _open = false;
                    return FileReply.Lost("the connection was cut");

                default:
                    _open = false;
                    return FileReply.Lost("a request got no ack in time");
            }
        }

        public void Close(string reason)
        {
            if (_open)
                Interlocked.Increment(ref Closes);

            _open = false;
        }

        public void Abort(string reason)
        {
            if (_open)
                Interlocked.Increment(ref Aborts);

            _open = false;
        }

        public void Dispose() => Abort("disposed");
    }


    internal sealed class FakeFileTransport : ILargeFileTransport
    {
        internal FakeFileTransport(FakeFileStore store) => FakeChannel = new FakeFileChannel(store);

        internal FakeFileChannel FakeChannel { get; }

        public IFileChannel Channel => FakeChannel;

        public long SessionNumber { get; set; } = 1;

        public TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(1);

        internal ConcurrentQueue<AddOrUpdateSensorRequest> Registrations { get; } = new ConcurrentQueue<AddOrUpdateSensorRequest>();

        internal Func<AddOrUpdateSensorRequest, LargeFileRegistration> Answer { get; set; } = _ => new LargeFileRegistration(RegistrationOutcome.Accepted, null);

        /// <summary>How the ordinary registration of a path stands (a routed send waits for true).</summary>
        internal Func<string, bool?> Registration { get; set; } = _ => true;

        public bool? RegistrationOf(string path) => Registration(path);

        public Task<LargeFileRegistration> RegisterAsync(AddOrUpdateSensorRequest request, CancellationToken token)
        {
            Registrations.Enqueue(request);
            return Task.FromResult(Answer(request));
        }
    }


    /// <summary>A service over the fakes, a temporary folder of files and a data folder; disposed with the test.</summary>
    internal sealed class LargeFileHarness : IDisposable
    {
        private readonly List<LargeFileService> _services = new List<LargeFileService>();

        internal LargeFileHarness(int pieceBytes = 65536)
        {
            Root = Path.Combine(Path.GetTempPath(), "hsm-large-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            Folder = Path.Combine(Root, "dumps");
            DataFolder = Path.Combine(Root, "data");
            Directory.CreateDirectory(Folder);
            Store = new FakeFileStore(pieceBytes);
            Transport = new FakeFileTransport(Store);
        }

        internal string Root { get; }
        internal string Folder { get; }
        internal string DataFolder { get; }
        internal FakeFileStore Store { get; }
        internal FakeFileTransport Transport { get; }
        internal VirtualTimings Timings { get; private set; } = new VirtualTimings();
        internal ConcurrentQueue<string> Errors { get; } = new ConcurrentQueue<string>();
        internal int Dropped;

        internal LargeFileService Service { get; private set; }

        /// <summary>A service, started; a second call is a restart of the collector on the same data folder.</summary>
        internal LargeFileService Start(bool unavailable = false)
        {
            Service?.Dispose();

            Timings = new VirtualTimings();
            Service = new LargeFileService(unavailable ? null : Transport, unavailable ? "this collector sends over HTTP" : null,
                                           new LargeFileStateStore(DataFolder, Errors.Enqueue), Registration, Errors.Enqueue,
                                           n => { Interlocked.Add(ref Dropped, (int)n); return true; }, null, Timings);
            _services.Add(Service);
            Service.Start();

            return Service;
        }

        /// <summary>A service the test makes itself (other delegates), on the harness's store, transport and a new clock; started.</summary>
        internal LargeFileService StartWith(Func<LargeFileStateStore, ILargeFileTransport, LargeFileService> make)
        {
            Service?.Dispose();

            Timings = new VirtualTimings();
            Service = make(new LargeFileStateStore(DataFolder, Errors.Enqueue), Transport);
            _services.Add(Service);
            Service.Start();

            return Service;
        }

        internal static AddOrUpdateSensorRequest Registration(string path, FileSensorOptions options) =>
            new AddOrUpdateSensorRequest { Path = "host/module/" + path.Trim('/'), SensorType = HSMSensorDataObjects.SensorType.FileSensor, DefaultAlertsOptions = options?.DefaultAlertsOptions ?? DefaultAlertsOptions.None };

        internal string WriteFile(string name, long size, uint seed = 7, DateTime? modified = null)
        {
            var path = Path.Combine(Folder, name);

            File.WriteAllBytes(path, SeededContent.Make(size, seed));
            File.SetLastWriteTimeUtc(path, modified ?? new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc));

            return path;
        }

        /// <summary>
        /// Moves the test's clock a second at a time until <paramref name="condition"/> holds: a wait the uploader set (a retry, a
        /// file put back) passes however late the uploader set it, so no test depends on when its thread got there.
        /// </summary>
        internal bool AdvanceUntil(Func<bool> condition, int seconds = 15)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);

            while (DateTime.UtcNow < until)
            {
                if (condition())
                    return true;

                Timings.Step(() => Service?.Wake());
            }

            return condition();
        }

        /// <summary>The test's clock forward a second at a time, each second looked at by the uploader.</summary>
        internal void Steps(int seconds)
        {
            for (var i = 0; i < seconds; i++)
                Timings.Step(() => Service?.Wake());
        }

        internal static bool Until(Func<bool> condition, int seconds = 15)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);

            while (DateTime.UtcNow < until)
            {
                if (condition())
                    return true;

                Thread.Sleep(10);
            }

            return condition();
        }

        // Garbage of a test is finalized at its end, not in a later test that watches finalizers (the .NET Framework
        // HttpListener behind the scripted server leaves faulted tasks of its own when a connection is cut).
        internal static void Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public void Dispose()
        {
            foreach (var service in _services)
                service.Dispose();

            Collect();

            try { Directory.Delete(Root, true); }
            catch (Exception) { }
        }
    }
}
