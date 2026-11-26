using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.LargeFiles
{
    /// <summary>
    /// Large files of a collector (ADR-0022, #1888): the watched folders, the handed-over files, and one uploader thread of
    /// lowered priority that takes them one at a time and sends them on the file connection of the session. Its public
    /// face is <see cref="Core.ILargeFileCollector"/> on the collector. Every method may be called from any thread; nothing
    /// throws into the host except the argument checks of the public calls.
    /// <para>
    /// One uploader by construction: one thread at most, made when there is a watch or a handed-over file and ended when
    /// there is none for a while. Stop and Dispose bump the generation and cut the upload; the thread checks its generation
    /// before any state write and any delete, so a thread that was blocked (a read on a hung share) does nothing when it
    /// wakes; a Start while it is still alive lets the same thread continue under the new generation.
    /// </para>
    /// </summary>
    internal sealed partial class LargeFileService : IDisposable
    {
        internal const string DroppedFilesQueueName = "Large files";

        private readonly ILargeFileTransport _transport;
        private readonly string _unavailable;
        private readonly Func<string, FileSensorOptions, AddOrUpdateSensorRequest> _registration;
        private readonly Action<string> _reportError;
        private readonly Func<long, bool> _countNotSent;
        private readonly ICollectorLogger _logger;
        private readonly LargeFileTimings _timings;
        private readonly LargeFileStateStore _store;

        private readonly object _gate = new object();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly List<LargeFileWatch> _watches = new List<LargeFileWatch>();
        private readonly List<Handed> _handed = new List<Handed>();
        private readonly Dictionary<string, LargeFileSensor> _sensors = new Dictionary<string, LargeFileSensor>(StringComparer.Ordinal);
        private readonly SaidOnce _said = new SaidOnce();

        // Under _gate.
        private Thread _thread;
        private bool _running;
        private bool _disposed;
        private long _watchOrder;
        private CancellationTokenSource _run = new CancellationTokenSource();
        private CancellationTokenSource _currentCut;
        private object _currentOwner;

        private long _generation;
        private long _runNumber;
        private bool _resetRunState;

        internal LargeFileService(ILargeFileTransport transport, string unavailableReason, LargeFileStateStore store,
                                  Func<string, FileSensorOptions, AddOrUpdateSensorRequest> registration,
                                  Action<string> reportError, Func<long, bool> countNotSent, ICollectorLogger logger, LargeFileTimings timings,
                                  Action<int> countDroppedFileValues = null, Action<HSMSensorDataObjects.SensorValueRequests.FileSensorValue> requeueFile = null)
        {
            _countDroppedFileValues = countDroppedFileValues;
            _requeueFile = requeueFile;
            _transport = transport;
            _unavailable = transport == null ? unavailableReason ?? "this collector has no collector session" : null;
            _store = store;
            _registration = registration;
            _reportError = reportError;
            _countNotSent = countNotSent;
            _logger = logger;
            _timings = timings ?? new LargeFileTimings();
        }

        /// <summary>For tests: the uploader's thread is alive.</summary>
        internal bool ThreadAlive
        {
            get
            {
                lock (_gate)
                    return _thread != null && _thread.IsAlive;
            }
        }

        /// <summary>For tests: files uploaded so far, stored or not, counted at their begin.</summary>
        internal long Begins => Interlocked.Read(ref _begins);

        private long _begins;


        // ---- The public calls ---------------------------------------------------------------------------------------

        internal ILargeFileWatch Watch(string sensorPath, string folder, string mask, LargeFileOptions options)
        {
            lock (_gate)
                if (_disposed)
                    return new LargeFileWatch(null, null, sensorPath, folder, mask, options ?? new LargeFileOptions(), LargeFileWatchState.Disposed, "the collector is disposed");

            CheckSensorPath(sensorPath);

            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("A watched folder needs a path.", nameof(folder));

            if (string.IsNullOrEmpty(mask))
                throw new ArgumentException("A watched folder needs a file name mask, for example *.dmp.", nameof(mask));

            // The subset both collectors match the same way (#1888 design §13.1): * and ? only.
            if (mask.IndexOfAny(new[] { '/', '\\', '[', ']', ';' }) >= 0)
                throw new ArgumentException("A file name mask is a name with * and ? only: it cannot hold a directory separator, '[', ']' or ';'.", nameof(mask));

            options = (options ?? new LargeFileOptions()).Snapshot();
            options.Validate();

            var full = FullPath(folder, nameof(folder)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (full.Length == 0)
                full = FullPath(folder, nameof(folder));

            if (_unavailable != null)
            {
                Say("unavailable", $"Large files cannot be sent: {_unavailable}.");
                return new LargeFileWatch(null, null, sensorPath, full, mask, options, LargeFileWatchState.Unavailable, _unavailable);
            }

            if (!_store.IsUsable(out var folderProblem))
                return new LargeFileWatch(null, null, sensorPath, full, mask, options, LargeFileWatchState.Unavailable, folderProblem);

            lock (_gate)
            {
                if (_disposed)
                    return new LargeFileWatch(null, null, sensorPath, full, mask, options, LargeFileWatchState.Disposed, "the collector is disposed");

                var sensor = SensorFor(sensorPath, options);
                var key = LargeFileStateStore.PathKey(full);

                if (_watches.Any(w => w.Sensor == sensor && LargeFileStateStore.PathKey(w.Folder) == key && w.Mask == mask))
                    throw new ArgumentException($"The folder {full} is already watched for {mask} into {sensorPath}; dispose that watch first.", nameof(folder));

                var watch = new LargeFileWatch(this, sensor, sensorPath, full, mask, options, LargeFileWatchState.Active, null) { Order = ++_watchOrder };

                _watches.Add(watch);
                EnsureThread();
                _wake.Set();

                return watch;
            }
        }

        internal Task<LargeFileResult> Send(string sensorPath, string filePath, LargeFileOptions options, CancellationToken token)
        {
            lock (_gate)
                if (_disposed)
                    return Task.FromResult(new LargeFileResult(LargeFileOutcome.Cancelled, "the collector is disposed"));

            CheckSensorPath(sensorPath);

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A large file needs a path.", nameof(filePath));

            options = (options ?? new LargeFileOptions()).Snapshot();
            options.Validate();

            var full = FullPath(filePath, nameof(filePath));

            if (_unavailable != null)
            {
                Say("unavailable", $"Large files cannot be sent: {_unavailable}.");
                return Task.FromResult(new LargeFileResult(LargeFileOutcome.Unavailable, $"large files cannot be sent: {_unavailable}"));
            }

            if (!_store.IsUsable(out var folderProblem))
                return Task.FromResult(new LargeFileResult(LargeFileOutcome.Unavailable, $"large files cannot be sent: {folderProblem}"));

            if (token.IsCancellationRequested)
                return Task.FromResult(new LargeFileResult(LargeFileOutcome.Cancelled, "the token was cancelled"));

            Handed item;

            lock (_gate)
            {
                if (_disposed)
                    return Task.FromResult(new LargeFileResult(LargeFileOutcome.Cancelled, "the collector is disposed"));

                var sensor = SensorFor(sensorPath, options);
                var key = LargeFileStateStore.PathKey(full);
                var pending = _handed.FirstOrDefault(h => h.Sensor == sensor && h.Key == key);

                if (pending != null)
                    return pending.Result.Task;

                item = new Handed(sensor, full, key, options);
                _handed.Add(item);
                EnsureThread();
                _wake.Set();
            }

            if (token.CanBeCanceled)
                item.Registration = token.Register(() => Cancel(item, "the token was cancelled"));

            return item.Result.Task;
        }

        // A path the system cannot take is a mistake of the call: always an ArgumentException (net472 throws others too).
        private static string FullPath(string path, string name)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is PathTooLongException || ex is System.Security.SecurityException)
            {
                throw new ArgumentException($"'{path}' is not a path this system can use: {ex.Message}", name, ex);
            }
        }

        private static void CheckSensorPath(string sensorPath)
        {
            if (string.IsNullOrWhiteSpace(sensorPath) || string.IsNullOrWhiteSpace(sensorPath.Trim('/')))
                throw new ArgumentException("A large file sensor needs a path.", nameof(sensorPath));

            if (sensorPath.IndexOf('\\') >= 0 || sensorPath.IndexOf('\t') >= 0)
                throw new ArgumentException("A sensor path cannot hold a backslash or a tab.", nameof(sensorPath));
        }

        // Under _gate. The first registration of a path stands; another one's options are not sent (said once).
        private LargeFileSensor SensorFor(string givenPath, LargeFileOptions options)
        {
            var request = _registration(givenPath, options.SensorOptions);

            if (options.NewFileAlert)
                request.DefaultAlertsOptions |= HSMSensorDataObjects.SensorRequests.DefaultAlertsOptions.LargeFiles;

            if (_sensors.TryGetValue(request.Path, out var known))
            {
                if (!SameRegistration(known.Request, request))
                    Say($"options:{request.Path}", $"The large file sensor {request.Path} was asked for again with other options; the first registration stands.");

                return known;
            }

            var ignored = IgnoredOptions(options.SensorOptions);

            if (ignored != null)
                Say($"ignored:{request.Path}", $"The large file sensor {request.Path} ignores {ignored}: they do not apply to large files.");

            var sensor = new LargeFileSensor(givenPath, request.Path, request);
            _sensors[request.Path] = sensor;
            return sensor;
        }

        private static bool SameRegistration(AddOrUpdateSensorRequest a, AddOrUpdateSensorRequest b) =>
            string.Equals(System.Text.Json.JsonSerializer.Serialize(a), System.Text.Json.JsonSerializer.Serialize(b), StringComparison.Ordinal);

        private static string IgnoredOptions(FileSensorOptions options)
        {
            if (options == null)
                return null;

            var names = new List<string>();
            var defaults = new FileSensorOptions();

            if (options.MaxFileSizeBytes != defaults.MaxFileSizeBytes) names.Add(nameof(options.MaxFileSizeBytes));
            if (options.DefaultFileName != null) names.Add(nameof(options.DefaultFileName));
            if (options.Extension != null) names.Add(nameof(options.Extension));
            if (options.IsPrioritySensor) names.Add(nameof(options.IsPrioritySensor));
            if (options.AggregateData.HasValue) names.Add(nameof(options.AggregateData));
            if (options.PreviousPaths != null && options.PreviousPaths.Count > 0) names.Add(nameof(options.PreviousPaths));

            return names.Count == 0 ? null : string.Join(", ", names);
        }

        internal void Unwatch(LargeFileWatch watch)
        {
            if (!watch.MarkDisposed())
                return;

            lock (_gate)
            {
                _watches.Remove(watch);

                if (ReferenceEquals(_currentOwner, watch))
                    CutCurrent();

                _wake.Set();
            }

            // The lock of its state is let go by the thread (it owns it); a thread that is gone lets it go here.
            ReleaseWatchLockIfIdle(watch);
        }

        private void Cancel(Handed item, string why)
        {
            lock (_gate)
            {
                if (!_handed.Remove(item))
                    return;

                if (ReferenceEquals(_currentOwner, item))
                    CutCurrent();
            }

            item.Complete(new LargeFileResult(LargeFileOutcome.Cancelled, why));
        }

        // Under _gate. Only an upload in progress is cut; an idle connection is closed politely by its owner.
        private void CutCurrent()
        {
            if (_currentCut == null)
                return;

            try { _currentCut.Cancel(); }
            catch (ObjectDisposedException) { }

            // A routed upload discards its part on the connection when it is cut; any other is cut with its connection.
            if (_currentOwner is Routed)
                return;

            try { _transport?.Channel.Abort("the upload was cut"); }
            catch (Exception) { }
        }


        // ---- Lifecycle ------------------------------------------------------------------------------------------------

        /// <summary>Uploads may run. Final refusals of the last run are forgotten; refused registrations are asked again.</summary>
        internal void Start()
        {
            lock (_gate)
            {
                if (_disposed || _running)
                    return;

                _running = true;

                // The run of the last Stop is over (its token was cancelled): its source goes.
                try { _run.Dispose(); } catch (Exception) { }

                _run = new CancellationTokenSource();
                Interlocked.Increment(ref _generation);
                Interlocked.Increment(ref _runNumber);

                foreach (var sensor in _sensors.Values)
                {
                    sensor.RegisteredOn = -1;
                    sensor.Refused = null;
                }

                foreach (var watch in _watches)
                    watch.Reactivate();

                _resetRunState = true;

                _said.Clear();

                // A data folder fixed between a Stop and this Start is noticed: the probe is taken again.
                _store.Reprobe();

                if (HasWorkUnderGate)
                    EnsureThread();

                _wake.Set();
            }
        }

        /// <summary>Cuts the upload in progress and waits for the thread at most a short bound. Never throws.</summary>
        internal void Stop()
        {
            Thread thread;

            lock (_gate)
            {
                if (!_running)
                    return;

                _running = false;
                Interlocked.Increment(ref _generation);

                try { _run.Cancel(); } catch (Exception) { }

                CutCurrent();
                thread = _thread;
                _wake.Set();
            }

            DropAllRouted("the collector stopped");

            // A routed upload that was cut discards its part on this connection first (its thread, within the wait below).
            WaitForThread(thread);

            try { _transport?.Channel.Close("the collector stopped"); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            Thread thread;
            List<Handed> handed;
            List<LargeFileWatch> watches;

            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _running = false;
                Interlocked.Increment(ref _generation);

                try { _run.Cancel(); } catch (Exception) { }

                CutCurrent();
                thread = _thread;
                handed = _handed.ToList();
                watches = _watches.ToList();
                _handed.Clear();
                _watches.Clear();
                _wake.Set();
            }

            foreach (var item in handed)
                item.Complete(new LargeFileResult(LargeFileOutcome.Cancelled, "the collector is disposed"));

            DropAllRouted("the collector was disposed");

            // Handed-over files are not kept across a restart: the ones still waiting are lost to this collector.
            if (handed.Count > 0)
            {
                Report($"{handed.Count} handed-over large file(s) were not sent: the collector was disposed before their upload ended. The files are left where they are.");

                foreach (var _ in handed)
                    CountNotSent();
            }

            foreach (var watch in watches)
                watch.MarkDisposed();

            try { _transport?.Channel.Abort("the collector is disposed"); }
            catch (Exception) { }

            WaitForThread(thread);

            foreach (var watch in watches)
                ReleaseWatchLockIfIdle(watch);

            // The thread is gone or was waited for its bound; a thread still blocked holds neither of these.
            if (thread == null || !thread.IsAlive)
            {
                try { _wake.Dispose(); } catch (Exception) { }
                try { _run.Dispose(); } catch (Exception) { }
            }
        }

        private void WaitForThread(Thread thread)
        {
            if (thread == null || thread == Thread.CurrentThread)
                return;

            try { thread.Join(_timings.StopWait); }
            catch (Exception) { }
        }

        private void ReleaseWatchLockIfIdle(LargeFileWatch watch)
        {
            if (!ThreadAlive)
            {
                watch.Lock?.Dispose();
                watch.Lock = null;
            }
        }

        // Under _gate.
        private void EnsureThread()
        {
            if (_disposed || (_thread != null && _thread.IsAlive))
                return;

            var thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "HSM large files",
            };

            _thread = thread;
            thread.Start();
        }

        // Under _gate: something the thread must stay for.
        private bool HasWorkUnderGate => _watches.Count > 0 || _handed.Count > 0 || _routed.Count > 0;

        /// <summary>For tests: a scan now (with a test clock that scans only when asked).</summary>
        internal void Wake() => _wake.Set();


        // ---- The thread -----------------------------------------------------------------------------------------------

        private void ThreadMain()
        {
            var idleSince = _timings.Now;

            try
            {
                while (true)
                {
                    long generation;
                    CancellationToken token;
                    bool running;
                    bool reset;
                    bool hasWork;

                    lock (_gate)
                    {
                        reset = _resetRunState;
                        _resetRunState = false;
                        hasWork = HasWorkUnderGate;

                        if (_disposed)
                            break;

                        // Nothing to do for a while: the thread ends, and is made again by the next watch or file.
                        if (!hasWork && _timings.Now - idleSince >= _timings.IdleExit)
                            break;

                        generation = Interlocked.Read(ref _generation);
                        running = _running;
                        token = _run.Token;
                    }

                    // What a run forgets at its Start, forgotten by the thread that owns it.
                    if (reset)
                    {
                        _refusedThisRun.Clear();
                        _notBefore.Clear();
                        _connectNotBefore = TimeSpan.Zero;
                        _connectNotBeforeSession = -1;
                        _connectAttempts = 0;
                        _keyRefused = null;
                        _linksSaid.Clear();
                        _inUse.Clear();
                        _sweptThisRun = false;
                    }

                    if (!running)
                    {
                        ReleaseDisposedLocks();
                        _timings.Wait(_timings.IdleExit, WaitKind.Scan, _wake, CancellationToken.None);
                        continue;
                    }

                    TimeSpan wait;

                    try
                    {
                        var work = Pick(generation, out wait);

                        if (work != null)
                        {
                            RunOne(work, generation, token);

                            // An upload may take longer than the idle time: idleness counts from its end.
                            idleSince = _timings.Now;
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Nothing above should throw; whatever does must not end the thread for good.
                        Say($"fault:{ex.GetType().Name}", $"The large file uploader failed: {ex.Message}. It goes on.");
                        wait = TimeSpan.FromSeconds(1);
                    }

                    if (hasWork)
                        idleSince = _timings.Now;

                    // The file connection is open only while a file is uploaded (ADR-0022 point 6): nothing to send now.
                    CloseIdleConnection();

                    _timings.Wait(wait, WaitKind.Scan, _wake, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Say("thread", $"The large file uploader's thread ended with an error: {ex.Message}.");
            }
            finally
            {
                ReleaseDisposedLocks();

                // The very last thing: until here a Watch or a Send sees this thread alive and makes no other, so two
                // threads never run; one that came while this one was ending gets a new thread now.
                lock (_gate)
                {
                    if (_thread == Thread.CurrentThread)
                        _thread = null;

                    if (!_disposed && HasWorkUnderGate)
                        EnsureThread();
                }
            }
        }

        private void CloseIdleConnection()
        {
            try
            {
                var channel = _transport?.Channel;

                if (channel != null && channel.IsOpen)
                    channel.Close("no file waits");
            }
            catch (Exception)
            {
            }
        }

        private void ReleaseDisposedLocks()
        {
            HashSet<LargeFileWatch> live;

            lock (_gate)
                live = new HashSet<LargeFileWatch>(_watches);

            foreach (var watch in _lockedWatches.ToList())
                if (watch.State == LargeFileWatchState.Disposed || !live.Contains(watch))
                {
                    watch.Lock?.Dispose();
                    watch.Lock = null;
                    _lockedWatches.Remove(watch);
                }
        }

        // Owned by the thread.
        private readonly List<LargeFileWatch> _lockedWatches = new List<LargeFileWatch>();
        private readonly HashSet<string> _linksSaid = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _inUse = new Dictionary<string, int>(StringComparer.Ordinal);
        private bool _sweptThisRun;

        // The server closed the file connection with 1008 (the access key can no longer send): a final refusal until the
        // next Start; nothing more is uploaded before it.
        private volatile string _keyRefused;
        // A final refusal of this run, by path, with the size and time of the file it was for: a new file under the name is new.
        private readonly Dictionary<string, (long Size, DateTime Modified)> _refusedThisRun = new Dictionary<string, (long, DateTime)>(StringComparer.Ordinal);
        private readonly Dictionary<string, TimeSpan> _notBefore = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);


        // ---- Picking --------------------------------------------------------------------------------------------------

        private sealed class Work
        {
            internal Routed Routed;
            internal Handed Handed;
            internal LargeFileWatch Watch;
            internal string Path;
            internal Observation Observation;
        }

        // The next file to upload: handed-over files first, in order; then the settled files of the watches, oldest first.
        // Null with the time to wait when there is none.
        private Work Pick(long generation, out TimeSpan wait)
        {
            // The lock of a disposed watch goes before anything is scanned: a new watch of the same folder takes it.
            ReleaseDisposedLocks();

            var now = _timings.Now;

            wait = TimeSpan.FromSeconds(10);

            if (_keyRefused != null)
            {
                wait = TimeSpan.FromSeconds(10);
                _timings.Scanned();
                return null;
            }

            // Every file needs the connection: while it may not be opened, nothing is picked (a wait for a new session is
            // polled, the session's number says when one came).
            if (ConnectionWaits(out var connectWait))
            {
                wait = _connectNotBeforeSession >= 0 ? Min(connectWait, TimeSpan.FromSeconds(1)) : connectWait;
                return null;
            }

            List<Handed> handed;
            List<LargeFileWatch> watches;
            List<Routed> routed;

            lock (_gate)
            {
                routed = _routed.ToList();
                handed = _handed.ToList();
                watches = _watches.Where(w => w.IsActive).OrderBy(w => w.Order).ToList();
            }

            // Routed sends first, in the order sent (amendment, decision 9).
            foreach (var item in routed)
            {
                if (_notBefore.TryGetValue(item.Key, out var at) && at > now)
                {
                    wait = Min(wait, at - now);
                    continue;
                }

                return new Work { Routed = item, Path = item.SensorPath };
            }

            foreach (var item in handed)
            {
                if (_notBefore.TryGetValue(item.Key, out var at) && at > now)
                {
                    wait = Min(wait, at - now);
                    continue;
                }

                return new Work { Handed = item, Path = item.FullPath };
            }

            var claimed = new HashSet<string>(StringComparer.Ordinal);
            Work best = null;

            foreach (var watch in watches)
            {
                if (now < watch.NextScan)
                {
                    wait = Min(wait, watch.NextScan - now);

                    // Its observations stand between scans; a candidate found by the last scan is still one.
                }
                else
                {
                    Scan(watch, now, generation);
                    watch.NextScan = now + _timings.ScanInterval(watch.Options.SettleTime);
                    wait = Min(wait, _timings.ScanInterval(watch.Options.SettleTime));
                }

                foreach (var pair in watch.Observations)
                {
                    var key = LargeFileStateStore.PathKey(pair.Key);

                    if (!claimed.Add(key))
                        continue;

                    var observation = pair.Value;

                    if (observation.KnownEntryConfirmed == true
                        || (_refusedThisRun.TryGetValue(key, out var refused) && refused.Size == observation.Size && refused.Modified == observation.Modified))
                        continue;

                    if (_notBefore.TryGetValue(key, out var at) && at > now)
                    {
                        wait = Min(wait, at - now);
                        continue;
                    }

                    var settledAt = observation.UnchangedSince + watch.Options.SettleTime;

                    if (settledAt > now)
                    {
                        wait = Min(wait, settledAt - now);
                        continue;
                    }

                    if (best == null || observation.Modified < best.Observation.Modified
                        || (observation.Modified == best.Observation.Modified && string.CompareOrdinal(pair.Key, best.Path) < 0))
                        best = new Work { Watch = watch, Path = pair.Key, Observation = observation };
                }
            }

            if (wait < TimeSpan.FromMilliseconds(50))
                wait = TimeSpan.FromMilliseconds(50);

            // For tests: the world as it is after this tick of the clock has been looked at.
            _timings.Scanned();

            return best;
        }

        private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

        // One poll of a watched folder: what is there, what changed, what is known.
        private void Scan(LargeFileWatch watch, TimeSpan now, long generation)
        {
            if (!EnsureWatchState(watch))
                return;

            ReportGivenUpOnce(watch);

            string[] files;

            try
            {
                files = Directory.Exists(watch.Folder) ? Directory.GetFiles(watch.Folder) : null;
            }
            catch (Exception ex)
            {
                files = null;
                FolderProblem(watch, $"the folder {watch.Folder} cannot be read: {ex.Message}");
            }

            if (files == null)
            {
                if (!Directory.Exists(watch.Folder))
                    FolderProblem(watch, $"the folder {watch.Folder} is not there");

                return;
            }

            if (watch.FolderProblem != null)
            {
                watch.FolderProblem = null;
                watch.SetReason(null);
                _logger?.Info($"The watched folder {watch.Folder} is readable again.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var path in files)
            {
                var name = Path.GetFileName(path);

                if (!watch.Matcher.Matches(name))
                    continue;

                FileInfo info;

                try
                {
                    info = new FileInfo(path);

                    if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0)
                        continue;

                    // A link is never followed: its target may lie outside the folder, and a delete through it would
                    // remove that. Said and counted once per run.
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        if (_linksSaid.Add(path))
                        {
                            watch.CountNotSent();
                            CountNotSent();
                            Report($"The large file {path} of the watch {watch} is a symbolic link or another reparse point: it is not sent.");
                        }

                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                seen.Add(path);

                long size;
                DateTime modified;

                try
                {
                    size = info.Length;
                    modified = info.LastWriteTimeUtc;
                }
                catch (Exception)
                {
                    continue;
                }

                if (!watch.Observations.TryGetValue(path, out var observation))
                {
                    observation = new Observation { Size = size, Modified = modified, UnchangedSince = now };
                    watch.Observations[path] = observation;
                }
                else if (observation.Size != size || observation.Modified != modified)
                {
                    observation.Size = size;
                    observation.Modified = modified;
                    observation.UnchangedSince = now;
                    observation.KnownEntryConfirmed = null;
                }

                if (observation.KnownEntryConfirmed == null)
                    observation.KnownEntryConfirmed = KnownEntryFor(watch, path, size, modified) ? (bool?)null : false;
            }

            foreach (var gone in watch.Observations.Keys.Where(k => !seen.Contains(k)).ToList())
                watch.Observations.Remove(gone);

            PruneState(watch, seen, generation);
        }

        // True when the state names a file of this path, size and time (delivered or given up): its identity is checked when
        // it is taken, which settles the observation one way or the other.
        private static bool KnownEntryFor(LargeFileWatch watch, string path, long size, DateTime modified)
        {
            var text = new FileFacts(null, size, modified, null).ModifiedText;

            return watch.Document.Delivered.Concat(watch.Document.Failing.Where(f => f.GivenUp != null))
                                 .Any(e => string.Equals(e.Path, path, StringComparison.Ordinal) && e.Size == size && e.Modified == text);
        }

        private void FolderProblem(LargeFileWatch watch, string problem)
        {
            if (watch.FolderProblem == problem)
                return;

            watch.FolderProblem = problem;
            watch.SetReason(problem);
            Say($"folder:{watch.Folder}:{problem}", $"The watch of {watch}: {problem}. It keeps looking; files are taken when the folder can be read.");
        }

        // The lock and the state of a watch, taken at its first scan; false while another process holds the lock.
        private bool EnsureWatchState(LargeFileWatch watch)
        {
            if (watch.Lock != null)
                return true;

            var held = _store.TryLock(watch.StateKey, TimeSpan.Zero);

            if (held == null)
            {
                if (!watch.LockHeldElsewhereSaid)
                {
                    watch.LockHeldElsewhereSaid = true;
                    watch.SetReason("another collector process watches the same folder, mask and sensor");
                    Say($"lock:{watch.StateKey}", $"The watch of {watch} waits: another collector process watches the same folder, mask and sensor (its state is locked in {_store.Root}).");
                }

                return false;
            }

            if (watch.LockHeldElsewhereSaid)
            {
                watch.LockHeldElsewhereSaid = false;
                watch.SetReason(null);
            }

            watch.Lock = held;
            _lockedWatches.Add(watch);

            var document = _store.ReadState(watch.StateKey);

            document.Folder = watch.Folder;
            document.Mask = watch.Mask;
            document.Sensor = watch.Sensor.Path;
            watch.Document = document;

            return true;
        }

        private void ReportGivenUpOnce(LargeFileWatch watch)
        {
            var run = Interlocked.Read(ref _runNumber);

            if (watch.GivenUpReportedRun == run)
                return;

            watch.GivenUpReportedRun = run;

            foreach (var entry in watch.Document.Failing.Where(e => e.GivenUp != null))
                Report($"The large file {entry.Path} of the watch {watch} is still on the disk unsent: the collector gave it up ({entry.GivenUp}). It is offered again when it changes.");
        }

        // Entries of files that are no longer in the folder are dropped, so the state does not grow.
        private void PruneState(LargeFileWatch watch, HashSet<string> present, long generation)
        {
            var before = watch.Document.Delivered.Count + watch.Document.Failing.Count;

            watch.Document.Delivered.RemoveAll(e => !present.Contains(e.Path));
            watch.Document.Failing.RemoveAll(e => !present.Contains(e.Path));

            if (watch.Document.Delivered.Count + watch.Document.Failing.Count != before && IsCurrent(generation))
                _store.WriteState(watch.StateKey, watch.Document);
        }

        private bool IsCurrent(long generation) => Interlocked.Read(ref _generation) == generation;

        private void CountNotSent()
        {
            try { _countNotSent?.Invoke(1); }
            catch (Exception) { }
        }


        // ---- Saying -----------------------------------------------------------------------------------------------------

        // Said once per run under this key.
        private void Say(string key, string text)
        {
            if (!_said.First(key))
                return;

            Report(text);
        }

        private void Report(string text)
        {
            try
            {
                if (_reportError != null)
                    _reportError(text);
                else
                    _logger?.Error(text);
            }
            catch (Exception)
            {
            }
        }


        /// <summary>A file handed over by <see cref="Send"/>.</summary>
        private sealed class Handed
        {
            internal Handed(LargeFileSensor sensor, string fullPath, string key, LargeFileOptions options)
            {
                Sensor = sensor;
                FullPath = fullPath;
                Key = key;
                Options = options;
            }

            internal LargeFileSensor Sensor { get; }
            internal string FullPath { get; }
            internal string Key { get; }
            internal LargeFileOptions Options { get; }
            internal TaskCompletionSource<LargeFileResult> Result { get; } = new TaskCompletionSource<LargeFileResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal CancellationTokenRegistration Registration;

            internal void Complete(LargeFileResult result)
            {
                Result.TrySetResult(result);

                try { Registration.Dispose(); }
                catch (Exception) { }
            }
        }
    }
}
