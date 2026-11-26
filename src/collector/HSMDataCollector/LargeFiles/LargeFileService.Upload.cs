using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using HSMDataCollector.Client.Session;
using HSMDataCollector.PublicInterface;

namespace HSMDataCollector.LargeFiles
{
    /// <summary>
    /// The upload of one file (#1888 design §3 with the binding changes of §12 and §13): begin, pieces, end on the file
    /// connection, every status of the wire contract handled as it says, the state written before the source is deleted.
    /// A routed send (an ordinary file value above the threshold, amendment of 2026-11-25) goes the same way from memory.
    /// </summary>
    internal sealed partial class LargeFileService
    {
        // Bounds that end in GivenUp (design §12.6, §13.1): a bug must not send 64 GB for ever.
        internal const int MaxDiscards = 3;
        internal const int MaxChecksumMismatches = 10;
        internal const int MaxMismatchesInARow = 3;
        internal const int MaxProtocolCloses = 3;

        /// <summary>A file held open by another program this many settle times in a row is said and counted once.</summary>
        internal const int InUseReportAfter = 10;

        /// <summary>The longest a server's <c>retryAfter</c> is waited for, in both collectors.</summary>
        internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(5);

        private static readonly ManualResetEvent Never = new ManualResetEvent(false);

        // Owned by the thread.
        private FileHello _hello;
        private int _connectAttempts;
        private TimeSpan _nextSendAt;
        private bool _slowLinkSaid;


        /// <summary>Why one upload stopped where it did.</summary>
        private enum Next
        {
            /// <summary>Settled: stored, refused, given up; nothing more to do with this file now.</summary>
            Done,
            /// <summary>Try the same file again after a wait (already set).</summary>
            Later,
            /// <summary>The connection was lost or must be opened again: begin again.</summary>
            Reconnect,
            /// <summary>Begin again on the same connection.</summary>
            Begin,
        }


        private sealed class Job
        {
            internal Work Work;
            internal LargeFileSensor Sensor;
            internal Options.LargeFileOptions Options;
            internal string Path;
            internal string Key;
            internal SourceFile File;
            internal FileFacts Facts;
            internal bool Secure = true;
            internal string StateKey;
            internal StateDocument Document;
            internal StateLock StateLock;
            internal StateLock RecordLock;
            internal string UploadKey;
            internal bool OwnRecord;
            internal long Generation;
            internal CancellationToken Token;

            // The upload as the server answered its begin.
            internal string Upload;
            internal byte[] UploadId;
            internal long Pieces;
            internal int PieceBytes;
            internal byte[] Digests;
            internal int MismatchesInARow;
            internal bool AskedSmallerPieces;
            internal int EndRounds;

            internal LargeFileWatch Watch => Work.Watch;
            internal bool IsHanded => Work.Handed != null;
            internal Routed Routed => Work.Routed;
            internal bool IsRouted => Work.Routed != null;

            internal string SensorPath => IsRouted ? Routed.SensorPath : Sensor.Path;

            internal StateEntry Failing()
            {
                var entry = Document.Failing.FirstOrDefault(e => e.Is(Path, Facts));

                if (entry == null)
                {
                    Document.Failing.RemoveAll(e => string.Equals(e.Path, Path, StringComparison.Ordinal));
                    entry = StateEntry.For(Path, Facts);
                    Document.Failing.Add(entry);
                }

                return entry;
            }

            internal bool ReadAt(long offset, byte[] buffer, int length)
            {
                if (!IsRouted)
                    return File.ReadAt(offset, buffer, length);

                if (offset + length > Routed.Size)
                    return false;

                Routed.Content.CopyTo((int)offset, buffer, 0, length);
                return true;
            }
        }


        private void RunOne(Work work, long generation, CancellationToken runToken)
        {
            using (var cut = CancellationTokenSource.CreateLinkedTokenSource(runToken))
            {
                lock (_gate)
                {
                    _currentCut = cut;
                    _currentOwner = (object)work.Routed ?? (object)work.Handed ?? work.Watch;
                }

                var job = new Job { Work = work, Path = work.Path, Key = work.Routed?.Key ?? LargeFileStateStore.PathKey(work.Path), Generation = generation, Token = cut.Token };

                try
                {
                    if (job.IsRouted)
                        UploadRouted(job);
                    else
                        Upload(job);
                }
                catch (OperationCanceledException) when (cut.IsCancellationRequested)
                {
                    if (job.IsRouted)
                    {
                        // Cut by Stop or Dispose: the value is dropped (counted by the stop); its part is discarded while
                        // the connection is still there, otherwise it expires on the server.
                        DiscardAfterCut(job);
                    }
                    else
                    {
                        // Cut by Stop, Dispose, the watch's Dispose or the caller's token: the part stays on the server.
                        _logger?.Info($"The upload of {job.Path} was cut; it continues from the server's piece when the file is offered again.");
                    }
                }
                catch (Exception ex)
                {
                    Report($"The upload of the large file {job.Path} failed: {ex.Message}. It is tried again later.");
                    Later(job, _transport.Backoff(_connectAttempts++));
                }
                finally
                {
                    lock (_gate)
                    {
                        _currentCut = null;
                        _currentOwner = null;
                    }

                    job.File?.Dispose();
                    job.RecordLock?.Dispose();

                    if (job.IsHanded)
                        job.StateLock?.Dispose();
                }
            }
        }

        private void Upload(Job job)
        {
            var handed = job.Work.Handed;

            job.Sensor = handed?.Sensor ?? job.Watch.Sensor;
            job.Options = handed?.Options ?? job.Watch.Options;
            job.Secure = job.Options.Secure;

            // ---- Open: no writer, not a link ----
            var opened = SourceFile.TryOpen(job.Path, job.Options.DeleteAfterUpload, out job.File, out var why);

            switch (opened)
            {
                case OpenFailure.None:
                    _inUse.Remove(job.Key);
                    break;

                case OpenFailure.InUse:
                    HeldOpen(job);

                    if (handed != null)
                        Later(job, _timings.ScanInterval(TimeSpan.FromSeconds(4)));
                    else
                        job.Work.Observation.UnchangedSince = _timings.Now;
                    return;

                case OpenFailure.Missing:
                case OpenFailure.Link:
                    if (handed != null)
                        NotSent(job, LargeFileOutcome.Missing, $"the large file {job.Path} cannot be sent: {why}");
                    else
                        job.Watch.Observations.Remove(job.Path);
                    return;

                default:
                    Final(job, $"the large file {job.Path} cannot be read: {why}");
                    return;
            }

            job.Facts = ReadFacts(job);

            // A watched file that changed since the scan waits for its settle time again.
            if (handed == null && (job.Facts.Size != job.Work.Observation.Size || job.Facts.ModifiedUtc != job.Work.Observation.Modified))
            {
                job.Work.Observation.Size = job.Facts.Size;
                job.Work.Observation.Modified = job.Facts.ModifiedUtc;
                job.Work.Observation.UnchangedSince = _timings.Now;
                job.Work.Observation.KnownEntryConfirmed = null;
                return;
            }

            // ---- The state: delivered, given up ----
            if (handed != null)
            {
                job.StateKey = LargeFileStateStore.SentKey(job.Sensor.Path);
                job.StateLock = _store.TryLock(job.StateKey, _timings.StateLockWait);

                if (job.StateLock == null)
                {
                    Say($"sentlock:{job.StateKey}", $"The large file {job.Path} waits: another collector process holds the state of the sensor {job.Sensor.Path}.");
                    Later(job, _timings.ScanInterval(TimeSpan.FromSeconds(40)));
                    return;
                }

                job.Document = _store.ReadState(job.StateKey);
                job.Document.Sensor = job.Sensor.Path;
            }
            else
            {
                job.StateKey = job.Watch.StateKey;
                job.Document = job.Watch.Document;
            }

            var delivered = job.Document.Delivered.FirstOrDefault(e => e.Is(job.Path, job.Facts));

            if (delivered != null)
            {
                if (!job.Options.DeleteAfterUpload)
                {
                    // Kept and delivered: not offered again.
                    if (handed == null)
                        job.Work.Observation.KnownEntryConfirmed = true;
                    else
                        Settle(job, new LargeFileResult(LargeFileOutcome.Stored, null, ParseTime(delivered.Stored), false));

                    return;
                }

                // Delivered, and not deleted (a crash between the state and the delete): the state's word alone deletes
                // nothing (#1888 design §13.1). A begin that answers `stored` in this run does; anything else uploads it.
                job.OwnRecord = true;
            }

            var givenUp = job.Document.Failing.FirstOrDefault(e => e.GivenUp != null && e.Is(job.Path, job.Facts));

            if (givenUp != null)
            {
                if (handed == null)
                    job.Work.Observation.KnownEntryConfirmed = true;
                else
                    NotSent(job, LargeFileOutcome.GivenUp, $"the large file {job.Path} was given up before: {givenUp.GivenUp}", count: false);

                return;
            }

            if (handed == null)
                job.Work.Observation.KnownEntryConfirmed = false;

            // ---- The in-progress record, held for the upload ----
            job.UploadKey = LargeFileStateStore.UploadKey(job.Sensor.Path, job.Path);
            job.RecordLock = _store.TryLock(job.UploadKey, TimeSpan.Zero);

            if (job.RecordLock == null)
            {
                Say($"recordlock:{job.UploadKey}", $"The large file {job.Path} waits: another collector process is uploading it.");
                Later(job, _timings.ScanInterval(TimeSpan.FromSeconds(40)));
                return;
            }

            SendLoop(job);
        }

        // A routed send: the content of an ordinary file value above the threshold, from memory (amendment of 2026-11-25).
        private void UploadRouted(Job job)
        {
            var routed = job.Routed;

            job.Facts = new FileFacts(routed.Name, routed.Size, routed.Time, routed.Source);
            job.Secure = false;
            job.Document = new StateDocument();
            job.Options = new Options.LargeFileOptions { DeleteAfterUpload = false, MaxBytesPerSecond = routed.MaxBytesPerSecond };

            // Its sensor's ordinary registration must have been acknowledged on the session: a begin must not overtake it.
            switch (_transport.RegistrationOf(routed.SensorPath))
            {
                case true:
                    break;

                case false:
                    DropRouted(job, $"its sensor {routed.SensorPath} was refused by the server");
                    return;

                default:
                    if (_timings.Now - routed.Queued > RoutedRegistrationWait)
                    {
                        DropRouted(job, $"the registration of its sensor {routed.SensorPath} was not acknowledged within {RoutedRegistrationWait.TotalMinutes:0} minutes");
                        return;
                    }

                    Later(job, TimeSpan.FromSeconds(1));
                    return;
            }

            SendLoop(job);
        }

        /// <summary>How long a routed send waits for its sensor's registration to be acknowledged.</summary>
        internal static readonly TimeSpan RoutedRegistrationWait = TimeSpan.FromMinutes(10);

        private void SendLoop(Job job)
        {
            var next = Next.Reconnect;

            while (true)
            {
                job.Token.ThrowIfCancellationRequested();

                if (!IsCurrent(job.Generation))
                    return;

                if (next == Next.Reconnect)
                {
                    if (!Connect(job))
                        return;

                    if (!job.IsRouted && !Register(job))
                        return;

                    if (_hello.MaxFileBytes > 0 && job.Facts.Size > _hello.MaxFileBytes)
                    {
                        Final(job, $"the large file {job.Path} has {job.Facts.Size} bytes, more than the server takes ({_hello.MaxFileBytes})");
                        return;
                    }

                    if (!_sweptThisRun)
                    {
                        _sweptThisRun = true;
                        SweepStaleRecords(job);
                    }

                    if (!job.IsRouted && !DiscardStaleUpload(job))
                        return;
                }

                next = BeginAndSend(job);

                if (next == Next.Done || next == Next.Later)
                    return;
            }
        }


        // ---- Connection ---------------------------------------------------------------------------------------------

        private bool Connect(Job job)
        {
            var channel = _transport.Channel;

            if (channel.IsOpen && _hello != null)
                return true;

            var opened = channel.OpenAsync(job.Token).GetAwaiter().GetResult();

            if (opened.IsOpen)
            {
                _hello = opened.Hello;
                _connectAttempts = 0;
                return true;
            }

            _hello = null;

            switch (opened.Status)
            {
                case 404:
                case 400:
                    // A server without large files (or with file connections off) answers the same until an operator
                    // changes it: said once, asked again with a new session and on a long period. Routed sends go back to
                    // the ordinary path for this session.
                    Say("absent", $"The server does not take large files ({opened.Why}): large files wait in their folders and are offered again every {_timings.FeatureAbsentRetry.TotalMinutes:0} minutes and with every new session.");
                    _connectNotBefore = _timings.Now + _timings.FeatureAbsentRetry;
                    _connectNotBeforeSession = _transport.SessionNumber;
                    RouteMissing(_transport.SessionNumber);
                    return false;

                case 409:
                    // The server knows no live session of this key under that id: the next session is the next chance.
                    _connectNotBefore = _timings.Now + _transport.Backoff(_connectAttempts++);
                    _connectNotBeforeSession = _transport.SessionNumber;
                    return false;

                default:
                    _logger?.Info($"The file connection could not be opened ({opened.Why}); trying again.");
                    _connectNotBefore = _timings.Now + _transport.Backoff(_connectAttempts++);
                    _connectNotBeforeSession = -1;
                    return false;
            }
        }

        private TimeSpan _connectNotBefore;
        private long _connectNotBeforeSession = -1;

        // A wait before the next connection, cut short by a new session when the wait is for one.
        private bool ConnectionWaits(out TimeSpan wait)
        {
            wait = _connectNotBefore - _timings.Now;

            if (wait <= TimeSpan.Zero)
                return false;

            if (_connectNotBeforeSession >= 0 && _transport.SessionNumber != _connectNotBeforeSession)
            {
                _connectNotBefore = TimeSpan.Zero;
                return false;
            }

            return true;
        }

        private bool Register(Job job)
        {
            var sensor = job.Sensor;

            if (sensor.Refused != null)
            {
                RegistrationRefused(job, sensor.Refused);
                return false;
            }

            if (sensor.RegisteredOn == _transport.SessionNumber)
                return true;

            var session = _transport.SessionNumber;
            var answer = _transport.RegisterAsync(sensor.Request, job.Token).GetAwaiter().GetResult();

            switch (answer.Outcome)
            {
                case RegistrationOutcome.Accepted:
                    sensor.RegisteredOn = session;
                    return true;

                case RegistrationOutcome.Refused:
                    sensor.Refused = answer.Reason;
                    RegistrationRefused(job, answer.Reason);
                    return false;

                default:
                    _logger?.Info($"The registration of the large file sensor {sensor.Path} was not answered ({answer.Reason}); trying again.");
                    Later(job, _transport.Backoff(_connectAttempts++));
                    return false;
            }
        }

        private void RegistrationRefused(Job job, string reason)
        {
            var text = $"The server refused the large file sensor {job.Sensor.Path}: {reason}. Its files are not sent before the collector's next start.";

            Say($"refusedsensor:{job.Sensor.Path}", text);

            List<LargeFileWatch> watches;

            lock (_gate)
                watches = _watches.Where(w => w.Sensor == job.Sensor).ToList();

            foreach (var watch in watches)
                if (watch.State == LargeFileWatchState.Active)
                    watch.MoveTo(LargeFileWatchState.Refused, reason);

            if (job.IsHanded)
                NotSent(job, LargeFileOutcome.Refused, $"the server refused the large file sensor {job.Sensor.Path}: {reason}");
        }


        // ---- Begin, pieces, end --------------------------------------------------------------------------------------

        private Next BeginAndSend(Job job)
        {
            var pieceBytes = job.AskedSmallerPieces ? _hello.PieceBytes : RecordedPieceBytes(job) ?? _hello.PieceBytes;
            var reply = Request(job, "begin", BeginBody(job.SensorPath, job.Facts, pieceBytes, job.Secure, job.Routed));

            if (!reply.Answered)
                return Lost(job, reply);

            var state = reply.Field("state");

            if (reply.Status == 200 && state == "stored")
            {
                // A file this collector sent before (an answer that was lost). The wire has no time here. Without an identity
                // the source goes only when this collector has its own record of having sent it.
                job.OwnRecord = job.OwnRecord || OwnUploadRecord(job);
                StoredAndSettle(job, null);
                return Next.Done;
            }

            if (reply.Status == 202)
            {
                Sleep(RetryAfter(reply), WaitKind.Retry, job);
                return Next.Begin;
            }

            if (reply.Status == 200)
            {
                var upload = reply.Field("upload");
                var id = PieceFrame.ParseUploadId(upload);
                var next = reply.LongField("next");
                var pieces = reply.LongField("pieces");
                var answeredPieceBytes = reply.LongField("pieceBytes") ?? pieceBytes;

                if (id == null || !next.HasValue || !pieces.HasValue || next < 0 || pieces < 0 || next > pieces || answeredPieceBytes <= 0
                    || pieces != (job.Facts.Size + answeredPieceBytes - 1) / answeredPieceBytes)
                    return ProtocolFault(job, $"the server answered begin with {reply}, which does not describe an upload of this file");

                // An upload begun under a larger hello: begin again with what this connection allows (the server then starts
                // from piece 0, as the contract says).
                if (answeredPieceBytes > _hello.PieceBytes && !job.AskedSmallerPieces)
                {
                    job.AskedSmallerPieces = true;
                    return Next.Begin;
                }

                Interlocked.Increment(ref _begins);

                if (job.IsRouted)
                    job.Routed.Began = true;

                PrepareUpload(job, upload, id, pieces.Value, (int)answeredPieceBytes);

                return SendPieces(job, next.Value);
            }

            if (reply.RetryAfter.HasValue && (reply.Status == 503 || reply.Status == 409))
            {
                // No room, too many uploads, a busy part, a fault of the store: the file waits and another may go.
                _logger?.Info($"The server cannot take the large file {job.Path} now ({reply}); trying again in {RetryAfter(reply).TotalSeconds:0} s.");
                Later(job, RetryAfter(reply));
                return Next.Later;
            }

            if (reply.Status >= 400 && reply.Status < 500)
            {
                Final(job, $"the server refused the large file {job.Path} ({reply})");
                return Next.Done;
            }

            Later(job, reply.RetryAfter.HasValue ? RetryAfter(reply) : _transport.Backoff(_connectAttempts++));
            return Next.Later;
        }

        private int? RecordedPieceBytes(Job job)
        {
            if (job.UploadKey == null)
                return null;

            var record = _store.ReadUpload(job.UploadKey);

            return record != null && record.Is(job.Facts, record.PieceBytes, job.Secure) && record.PieceBytes <= _hello.PieceBytes ? record.PieceBytes : (int?)null;
        }

        private bool OwnUploadRecord(Job job)
        {
            if (job.UploadKey == null)
                return job.IsRouted;

            var record = _store.ReadUpload(job.UploadKey);

            return record != null && record.IsFile(job.Facts);
        }

        // The record and the digests of the upload the server answered; a list of another upload or another cut is dropped.
        private void PrepareUpload(Job job, string upload, byte[] id, long pieces, int pieceBytes)
        {
            job.Upload = upload;
            job.UploadId = id;
            job.Pieces = pieces;
            job.PieceBytes = pieceBytes;
            job.OwnRecord = true;

            if (job.UploadKey == null)
            {
                job.Digests = new byte[checked((int)(pieces * 32))];
                return;
            }

            var record = _store.ReadUpload(job.UploadKey);
            var same = record != null && record.Is(job.Facts, pieceBytes, job.Secure) && string.Equals(record.Upload, upload, StringComparison.Ordinal) && record.Pieces == pieces;

            if (same)
            {
                job.Digests = _store.ReadDigests(job.UploadKey, pieces);
                return;
            }

            _store.DeleteUpload(job.UploadKey);
            job.Digests = new byte[checked((int)(pieces * 32))];

            if (IsCurrent(job.Generation))
                _store.WriteUpload(job.UploadKey, new UploadRecord
                {
                    Path = job.Path,
                    Sensor = job.Sensor.Path,
                    Upload = upload,
                    Name = job.Facts.Name,
                    Size = job.Facts.Size,
                    Modified = job.Facts.ModifiedText,
                    Source = job.Facts.Source,
                    PieceBytes = pieceBytes,
                    Pieces = pieces,
                    Secure = job.Secure,
                });
        }

        private Next SendPieces(Job job, long next)
        {
            using (var frame = new PieceFrame(job.PieceBytes))
            {
                var n = next;

                while (n < job.Pieces)
                {
                    job.Token.ThrowIfCancellationRequested();

                    // A routed send waiting goes first (amendment, decision 9): this file begins again after it and continues
                    // from the server's piece.
                    if (!job.IsRouted && RoutedWaiting())
                    {
                        Later(job, TimeSpan.Zero);
                        return Next.Later;
                    }

                    var length = (int)Math.Min(job.PieceBytes, job.Facts.Size - n * job.PieceBytes);

                    if (!job.ReadAt(n * job.PieceBytes, frame.Piece, length))
                        return Changed(job, "it became shorter while it was sent");

                    // Read, compress, wait for this frame's bytes under the limit, send (#1888 design §13.1).
                    frame.Build(0, job.UploadId, n, length);
                    Throttle(job, frame.FrameLength);

                    var reply = _transport.Channel.SendPieceAsync(frame.Frame, frame.FrameLength, job.Token).GetAwaiter().GetResult();

                    CheckLinkFloor(frame.FrameLength);

                    if (!reply.Answered)
                        return Lost(job, reply);

                    var answeredNext = reply.LongField("next");

                    if (reply.Status == 200 && answeredNext.HasValue)
                    {
                        SetDigest(job, n, frame.Digest);
                        job.MismatchesInARow = 0;

                        if (answeredNext.Value < 0 || answeredNext.Value > job.Pieces)
                            return ProtocolFault(job, $"the server answered piece {n} with next {answeredNext.Value} of {job.Pieces}");

                        n = answeredNext.Value;
                        continue;
                    }

                    if (reply.Status == 409 && answeredNext.HasValue && reply.Field("reason") == "checksum mismatch")
                    {
                        var failing = job.Failing();

                        failing.ChecksumMismatches++;
                        job.MismatchesInARow++;
                        WriteState(job);

                        if (failing.ChecksumMismatches >= MaxChecksumMismatches)
                            return GiveUp(job, $"the server found a checksum mismatch in {failing.ChecksumMismatches} pieces");

                        if (job.MismatchesInARow >= MaxMismatchesInARow)
                        {
                            _transport.Channel.Abort("three checksum mismatches in a row");
                            job.MismatchesInARow = 0;
                            return Next.Reconnect;
                        }

                        continue;
                    }

                    if (reply.Status == 409 && answeredNext.HasValue)
                    {
                        // Out of order: go on from the server's next.
                        if (answeredNext.Value < 0 || answeredNext.Value > job.Pieces)
                            return ProtocolFault(job, $"the server answered piece {n} with next {answeredNext.Value} of {job.Pieces}");

                        n = answeredNext.Value;
                        continue;
                    }

                    if (reply.Status == 503 && reply.RetryAfter.HasValue)
                    {
                        Sleep(RetryAfter(reply), WaitKind.Retry, job);
                        continue;
                    }

                    if (reply.Status == 404)
                        return Next.Begin;

                    if (reply.Status == 400)
                        return Discarded(job, reply);

                    if (reply.Status >= 400 && reply.Status < 500)
                    {
                        Final(job, $"the server refused a piece of the large file {job.Path} ({reply})");
                        return Next.Done;
                    }

                    Sleep(reply.RetryAfter.HasValue ? RetryAfter(reply) : _transport.Backoff(_connectAttempts++), WaitKind.Retry, job);
                }
            }

            return End(job);
        }

        private Next End(Job job)
        {
            // A file that changed is not finished (ADR-0022 point 3).
            if (!ReadFacts(job).Equals(job.Facts))
                return Changed(job, "it changed while it was sent");

            if (!FillDigests(job))
                return Changed(job, "it changed while it was sent");

            var body = JsonSerializer.Serialize(new Dictionary<string, object> { ["upload"] = job.Upload, ["hash"] = PieceFrame.FileHash(job.Digests, job.Pieces) });

            while (true)
            {
                job.Token.ThrowIfCancellationRequested();

                var reply = Request(job, "end", body);

                if (!reply.Answered)
                    return Lost(job, reply);

                if (reply.Status == 202)
                {
                    Sleep(RetryAfter(reply), WaitKind.Retry, job);
                    continue;
                }

                if (reply.Status == 200 && reply.Field("state") == "stored")
                {
                    StoredAndSettle(job, ParseTime(reply.Field("time")));
                    return Next.Done;
                }

                // Pieces are missing: the answer may come from the check, after which this connection holds no upload; a begin
                // holds it again and says where to go on from.
                if (reply.Status == 409 && reply.LongField("next").HasValue)
                {
                    if (++job.EndRounds > MaxProtocolCloses * 3)
                        return ProtocolFault(job, $"the server answered end with missing pieces {job.EndRounds} times");

                    return Next.Begin;
                }

                if (reply.Status == 400)
                    return Discarded(job, reply);

                if (reply.Status == 404)
                    return Next.Begin;

                if (reply.Status == 503 && reply.RetryAfter.HasValue)
                {
                    Sleep(RetryAfter(reply), WaitKind.Retry, job);
                    return Next.Begin;
                }

                if (reply.Status >= 400 && reply.Status < 500)
                {
                    Final(job, $"the server refused the end of the large file {job.Path} ({reply})");
                    return Next.Done;
                }

                Sleep(reply.RetryAfter.HasValue ? RetryAfter(reply) : _transport.Backoff(_connectAttempts++), WaitKind.Retry, job);
                return Next.Begin;
            }
        }

        private void SetDigest(Job job, long number, byte[] digest)
        {
            Buffer.BlockCopy(digest, 0, job.Digests, checked((int)(number * 32)), 32);

            if (job.UploadKey != null && IsCurrent(job.Generation))
                _store.WriteDigest(job.UploadKey, job.Pieces, number, digest);
        }

        // Digests the list lacks (a crash between an ack and its write, a next that jumped ahead) come from reading those
        // pieces again: not counted by the speed limit, on this same thread. False when the file changed meanwhile.
        private bool FillDigests(Job job)
        {
            byte[] buffer = null;
            PieceFrame hasher = null;

            try
            {
                for (long n = 0; n < job.Pieces; n++)
                {
                    if (!IsZero(job.Digests, n))
                        continue;

                    job.Token.ThrowIfCancellationRequested();

                    if (buffer == null)
                    {
                        buffer = new byte[job.PieceBytes];
                        hasher = new PieceFrame(64);
                    }

                    var length = (int)Math.Min(job.PieceBytes, job.Facts.Size - n * job.PieceBytes);

                    if (!job.ReadAt(n * job.PieceBytes, buffer, length))
                        return false;

                    SetDigest(job, n, hasher.ComputeDigest(buffer, length));
                }

                return ReadFacts(job).Equals(job.Facts);
            }
            finally
            {
                hasher?.Dispose();
            }
        }

        private static bool IsZero(byte[] digests, long number)
        {
            var offset = number * 32;

            for (var i = 0; i < 32; i++)
                if (digests[offset + i] != 0)
                    return false;

            return true;
        }


        // ---- What the answers lead to --------------------------------------------------------------------------------

        private FileFacts ReadFacts(Job job) => job.IsRouted ? job.Facts : _timings.Observe(job.Path, job.File.ReadFacts());

        private FileReply Request(Job job, string op, string body) => _transport.Channel.RequestAsync(op, body, job.Token).GetAwaiter().GetResult();

        // A lost connection. The server's close says how: 1008, the key can no longer send, is a final refusal until the next
        // Start; 1002, 1007 and 1009 say a frame of ours was wrong and count against the file.
        private Next Lost(Job job, FileReply reply)
        {
            job.Token.ThrowIfCancellationRequested();

            _transport.Channel.Abort("the file connection was lost");

            if (reply.ServerClose == 1008)
            {
                _keyRefused = reply.Why ?? "closed with 1008";
                Say("1008", $"The server closed the file connection with 1008: the access key can no longer send. Large files are not sent before the collector's next start ({reply.Why}).");
                Final(job, $"the server closed the file connection during the upload of {job.Path} with 1008: the access key can no longer send");
                return Next.Done;
            }

            if (reply.ServerClose == 1002 || reply.ServerClose == 1007 || reply.ServerClose == 1009)
            {
                var failing = job.Failing();

                failing.ProtocolCloses++;
                WriteState(job);

                Report($"The server closed the file connection with {reply.ServerClose} during the upload of {job.Path} ({reply.Why}): a frame of this collector was wrong.");

                if (failing.ProtocolCloses >= MaxProtocolCloses)
                {
                    GiveUp(job, $"the server closed the file connection {failing.ProtocolCloses} times as a protocol error ({reply.ServerClose})");
                    return Next.Done;
                }
            }

            Sleep(_transport.Backoff(_connectAttempts++), WaitKind.Retry, job);
            return Next.Reconnect;
        }

        private Next ProtocolFault(Job job, string why)
        {
            Report($"The file connection is not trusted any more: {why}. It is opened again.");
            _transport.Channel.Abort(why);

            var failing = job.Failing();

            failing.ProtocolCloses++;
            WriteState(job);

            if (failing.ProtocolCloses >= MaxProtocolCloses)
                return GiveUp(job, why);

            Sleep(_transport.Backoff(_connectAttempts++), WaitKind.Retry, job);
            return Next.Reconnect;
        }

        // The server removed the part (a piece or the whole file did not check out): begin again from 0, at most three times.
        private Next Discarded(Job job, FileReply reply)
        {
            var failing = job.Failing();

            failing.Discards++;
            WriteState(job);

            if (job.UploadKey != null && IsCurrent(job.Generation))
                _store.DeleteUpload(job.UploadKey);

            Report($"The server discarded the upload of {job.Path} ({reply}); it is sent again from the start ({failing.Discards} of {MaxDiscards}).");

            if (failing.Discards >= MaxDiscards)
                return GiveUp(job, $"the server discarded the upload {failing.Discards} times, the last: {reply.Error ?? reply.ToString()}");

            return Next.Begin;
        }

        // The file changed while it was sent: its upload is discarded on the server and it is taken again when it settles.
        // The record goes only when the server confirmed the discard; otherwise the next pick, or the sweep at Start, does it.
        private Next Changed(Job job, string why)
        {
            _logger?.Info($"The large file {job.Path} is sent again when it has settled: {why}.");

            var record = job.UploadKey == null ? null : _store.ReadUpload(job.UploadKey);
            var original = record != null ? FactsOf(record) : job.Facts;

            if (DiscardOnServer(job, job.Upload, job.SensorPath, original, record?.PieceBytes ?? job.PieceBytes, job.Secure, job.Routed, job.Token) && job.UploadKey != null && IsCurrent(job.Generation))
                _store.DeleteUpload(job.UploadKey);

            if (job.IsHanded)
                Later(job, _timings.ScanInterval(TimeSpan.FromSeconds(4)));
            else if (!job.IsRouted)
            {
                job.Work.Observation.UnchangedSince = _timings.Now;
                job.Work.Observation.KnownEntryConfirmed = null;
            }

            return Next.Done;
        }

        private static FileFacts FactsOf(UploadRecord record) =>
            new FileFacts(record.Name, record.Size, ParseTime(record.Modified) ?? DateTime.MinValue, record.Source);

        /// <summary>
        /// <c>discard</c> of an upload (wire contract, "discard"). True when the part is gone: discarded, or a begin with the
        /// original facts said it was stored or there is nothing to continue. A 404 (this connection does not hold it) is
        /// answered by a begin with the original facts and a discard. An older server answers <c>400 unknown op</c>: said
        /// once, the part expires on the server (true: nothing more can be done).
        /// </summary>
        private bool DiscardOnServer(Job job, string upload, string sensorPath, FileFacts original, int pieceBytes, bool secure, Routed routed, CancellationToken token)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (upload == null)
                {
                    var begun = _transport.Channel.RequestAsync("begin", BeginBody(sensorPath, original, pieceBytes, secure, routed), token).GetAwaiter().GetResult();

                    if (!begun.Answered)
                        return false;

                    if (begun.Status == 200 && begun.Field("state") == "stored")
                        return true;

                    if (begun.Status == 200 && begun.Field("upload") is string held)
                    {
                        upload = held;
                        continue;
                    }

                    if (begun.Status == 202 || begun.RetryAfter.HasValue)
                        return false;

                    // A refusal: the server holds no part of it that this collector can reach.
                    return true;
                }

                var reply = _transport.Channel.RequestAsync("discard", JsonSerializer.Serialize(new Dictionary<string, object> { ["upload"] = upload }), token).GetAwaiter().GetResult();

                if (!reply.Answered)
                    return false;

                if (reply.Status == 200)
                    return true;

                if (reply.Status == 404)
                {
                    upload = null;
                    continue;
                }

                if (reply.Status == 400)
                {
                    Say("nodiscard", $"The server cannot discard an upload ({reply}): the part of a changed file stays on the server until it expires.");
                    return true;
                }

                if (reply.Status == 409)
                {
                    // Its end check is running: the check settles it; a begin afterwards says stored or where to go on from.
                    _timings.Wait(RetryAfter(reply), WaitKind.Retry, Never, token);
                    upload = null;
                    continue;
                }

                if (reply.Status == 503)
                {
                    _timings.Wait(RetryAfter(reply), WaitKind.Retry, Never, token);
                    continue;
                }

                return false;
            }

            return false;
        }

        // An upload record of an earlier version of this file (it changed while the collector was down): begin it with its
        // original facts and discard it, so its reserved space and its slot go now, not after 7 days.
        private bool DiscardStaleUpload(Job job)
        {
            var record = _store.ReadUpload(job.UploadKey);

            if (record == null || record.IsFile(job.Facts))
                return true;

            if (DiscardOnServer(job, null, job.Sensor.Path, FactsOf(record), record.PieceBytes, record.Secure, null, job.Token))
            {
                if (IsCurrent(job.Generation))
                    _store.DeleteUpload(job.UploadKey);

                return true;
            }

            // Not settled now (a cut, a check running): the record stays, the file is tried again later.
            if (!_transport.Channel.IsOpen)
                Sleep(_transport.Backoff(_connectAttempts++), WaitKind.Retry, job);

            Later(job, _timings.ScanInterval(TimeSpan.FromSeconds(40)));
            return false;
        }

        // Once a run: records of files that are gone or changed are discarded on the server (design §3), so their parts do not
        // keep space and slots for 7 days. Records of files that are there unchanged stay: their uploads continue.
        private void SweepStaleRecords(Job job)
        {
            foreach (var key in _store.UploadKeys())
            {
                if (key == job.UploadKey || !IsCurrent(job.Generation))
                    continue;

                using (var held = _store.TryLock(key, TimeSpan.Zero))
                {
                    if (held == null)
                        continue;

                    var record = _store.ReadUpload(key);

                    if (record == null || record.Path == null || record.Sensor == null)
                        continue;

                    if (StillThere(record))
                        continue;

                    if (DiscardOnServer(job, null, record.Sensor, FactsOf(record), record.PieceBytes, record.Secure, null, job.Token))
                    {
                        if (IsCurrent(job.Generation))
                            _store.DeleteUpload(key);

                        _logger?.Info($"The part of {record.Path} on the server was discarded: the file is gone or changed.");
                    }
                    else if (!_transport.Channel.IsOpen)
                    {
                        return;
                    }
                }
            }
        }

        // The file of a record is where it was, with its size and time (its identity is compared when it is taken).
        private static bool StillThere(UploadRecord record)
        {
            try
            {
                var info = new FileInfo(record.Path);

                return info.Exists && info.Length == record.Size
                    && new FileFacts(null, info.Length, info.LastWriteTimeUtc, null).ModifiedText == record.Modified;
            }
            catch (Exception)
            {
                return true;
            }
        }

        // A routed upload cut by Stop or Dispose: its part is discarded while the connection is still there.
        private void DiscardAfterCut(Job job)
        {
            if (job.Upload == null || !_transport.Channel.IsOpen)
                return;

            try
            {
                using (var bound = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
                    DiscardOnServer(job, job.Upload, job.SensorPath, job.Facts, job.PieceBytes, false, job.Routed, bound.Token);
            }
            catch (Exception)
            {
                // The part expires on the server.
            }
        }

        private void StoredAndSettle(Job job, DateTime? time)
        {
            if (!IsCurrent(job.Generation))
                return;

            if (job.IsRouted)
            {
                CompleteRouted(job);
                _logger?.Info($"The file value of {job.SensorPath} ({job.Facts.Size} bytes) is stored on the server as a large file value.");
                return;
            }

            // The state first: a crash after it and before the delete finds the entry (and a begin confirms it before a delete).
            job.Document.Failing.RemoveAll(e => string.Equals(e.Path, job.Path, StringComparison.Ordinal));
            job.Document.Delivered.RemoveAll(e => string.Equals(e.Path, job.Path, StringComparison.Ordinal));

            var entry = StateEntry.For(job.Path, job.Facts);

            entry.Stored = time?.ToString("o", CultureInfo.InvariantCulture);
            job.Document.Delivered.Add(entry);
            WriteState(job);

            if (job.UploadKey != null)
                _store.DeleteUpload(job.UploadKey);

            var deleted = false;
            string note = null;

            if (job.Options.DeleteAfterUpload)
            {
                var now = ReadFacts(job);

                if (!now.Equals(job.Facts))
                {
                    note = $"the source {job.Path} was not deleted: it changed after it was sent";
                    _logger?.Info($"The large file {note}.");
                }
                else if (job.Facts.Source == null && !job.OwnRecord)
                {
                    note = $"the source {job.Path} was not deleted: the server has a file like it, and this collector has no record of having sent this one (there is no file identity to tell them apart)";
                    Report($"The large file {note}.");
                }
                else if (job.IsHanded && !IsPending(job.Work.Handed))
                {
                    // The caller cancelled the hand-over meanwhile and was told so: nothing of it is deleted.
                    note = $"the source {job.Path} was not deleted: the hand-over was cancelled";
                }
                else if (IsCurrent(job.Generation))
                {
                    try
                    {
                        job.File.DeleteAndClose();
                        deleted = true;
                    }
                    catch (Exception ex)
                    {
                        note = $"the source {job.Path} could not be deleted: {ex.Message}";
                        Report($"The large file {note}.");
                    }
                }
            }

            // Deleted: the entry only covered the moment between the state and the delete.
            if (deleted)
            {
                job.Document.Delivered.Remove(entry);
                WriteState(job);
            }

            _logger?.Info($"The large file {job.Path} is stored on the server{(deleted ? " and deleted here" : string.Empty)}.");

            if (job.IsHanded)
                Settle(job, new LargeFileResult(LargeFileOutcome.Stored, note, time, deleted));
            else
            {
                job.Watch.CountStored();

                if (!deleted)
                    job.Work.Observation.KnownEntryConfirmed = true;
            }
        }

        private bool IsPending(Handed item)
        {
            lock (_gate)
                return _handed.Contains(item);
        }

        private void Settle(Job job, LargeFileResult result)
        {
            Remove(job.Work.Handed);
            job.Work.Handed.Complete(result);
        }

        private void Final(Job job, string reason)
        {
            if (job.IsRouted)
            {
                DropRouted(job, reason);
                return;
            }

            if (job.IsHanded)
            {
                NotSent(job, LargeFileOutcome.Refused, reason);
                return;
            }

            // The facts may not have been read (a file that cannot be opened): the observation's size and time stand for them.
            var observation = job.Work.Observation;

            _refusedThisRun[job.Key] = job.Facts.Name != null ? (job.Facts.Size, job.Facts.ModifiedUtc) : (observation.Size, observation.Modified);
            job.Watch.SetReason(reason);
            NotSent(job, LargeFileOutcome.Refused, reason);
        }

        private Next GiveUp(Job job, string reason)
        {
            if (job.IsRouted)
            {
                DropRouted(job, $"the collector gave it up: {reason}");
                return Next.Done;
            }

            var failing = job.Failing();

            failing.GivenUp = reason;
            WriteState(job);

            if (job.UploadKey != null && IsCurrent(job.Generation))
                _store.DeleteUpload(job.UploadKey);

            if (!job.IsHanded)
                job.Work.Observation.KnownEntryConfirmed = true;

            NotSent(job, LargeFileOutcome.GivenUp, $"the collector gave up the large file {job.Path}: {reason}; it is left where it is and offered again when it changes");
            return Next.Done;
        }

        // A file not sent: said once, counted in the watch (or the hand-over) and in Queue overflow "Large files".
        private void NotSent(Job job, LargeFileOutcome outcome, string reason, bool count = true)
        {
            Say($"notsent:{job.Key}:{job.Facts}:{outcome}", char.ToUpperInvariant(reason[0]) + reason.Substring(1) + ".");

            if (count)
            {
                job.Watch?.CountNotSent();
                CountNotSent();
            }

            if (job.IsHanded)
                Settle(job, new LargeFileResult(outcome, reason));
        }

        // Another program holds the file without sharing it: said and counted once after InUseReportAfter tries in a row.
        private void HeldOpen(Job job)
        {
            _inUse.TryGetValue(job.Key, out var tries);
            _inUse[job.Key] = ++tries;

            if (tries != InUseReportAfter)
                return;

            job.Watch?.CountNotSent();
            CountNotSent();
            Report($"The large file {job.Path} is held open by another program and has not been sent after {tries} tries; it is taken when it is released.");
        }

        private void Remove(Handed item)
        {
            lock (_gate)
                _handed.Remove(item);
        }

        private void Later(Job job, TimeSpan wait)
        {
            _notBefore[job.Key] = _timings.Now + (wait < TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50) : wait);
        }

        private void WriteState(Job job)
        {
            if (!IsCurrent(job.Generation) || job.StateKey == null)
                return;

            // The state of handed-over files names files anywhere: those that are gone are dropped, so it does not grow.
            if (job.IsHanded)
            {
                job.Document.Delivered.RemoveAll(e => !FileExists(e.Path));
                job.Document.Failing.RemoveAll(e => !FileExists(e.Path));
            }

            _store.WriteState(job.StateKey, job.Document);
        }

        private static bool FileExists(string path)
        {
            try { return File.Exists(path); }
            catch (Exception) { return true; }
        }

        private void Sleep(TimeSpan span, WaitKind kind, Job job)
        {
            _timings.Wait(span, kind, Never, job.Token);
            job.Token.ThrowIfCancellationRequested();
        }


        // ---- Speed limit, slow links ---------------------------------------------------------------------------------

        // Waited for before a frame, never inside one: the frame then goes at full speed and meets the server's deadline. The
        // frame's own bytes (88 + compressed) are what it costs (#1888 design §13.1).
        private void Throttle(Job job, int frameBytes)
        {
            var limit = job.Options.MaxBytesPerSecond;

            if (limit <= 0)
                return;

            var now = _timings.Now;
            var start = _nextSendAt > now ? _nextSendAt : now;

            if (start > now)
                Sleep(start - now, WaitKind.Throttle, job);

            _nextSendAt = start + TimeSpan.FromTicks((long)(frameBytes * (double)TimeSpan.TicksPerSecond / limit));
        }

        private void CheckLinkFloor(int frameBytes)
        {
            var hello = _hello;

            if (hello == null || _slowLinkSaid)
                return;

            var deadline = TimeSpan.FromSeconds(hello.HeartbeatSeconds * 3);
            var took = _transport.Channel.LastPieceWrite;

            if (took < deadline)
                return;

            _slowLinkSaid = true;

            var rate = frameBytes / Math.Max(1, took.TotalSeconds) / 1024;
            var needs = hello.MaxFrameBytes / deadline.TotalSeconds / 1024;

            Report($"The link to the server passes about {rate:0} KB/s: a piece frame took {took.TotalSeconds:0} s to write, and the server cuts a frame that takes more than {deadline.TotalSeconds:0} s (a frame of up to {hello.MaxFrameBytes} bytes needs at least {needs:0} KB/s). Large files keep trying.");
        }


        // ---- Wire bodies -----------------------------------------------------------------------------------------------

        private static string BeginBody(string sensorPath, FileFacts facts, int pieceBytes, bool secure, Routed routed)
        {
            var body = new Dictionary<string, object>
            {
                ["path"] = sensorPath,
                ["name"] = facts.Name,
                ["size"] = facts.Size,
                ["modified"] = facts.ModifiedText,
            };

            if (facts.Source != null)
                body["source"] = facts.Source;

            body["pieceBytes"] = pieceBytes;
            body["secure"] = secure;

            // A routed send keeps the status and the comment of its value (amendment, decision 6).
            if (routed != null)
            {
                if (routed.Status != (int)HSMSensorDataObjects.SensorStatus.Ok)
                    body["status"] = routed.Status;

                if (!string.IsNullOrEmpty(routed.Comment))
                    body["comment"] = routed.Comment;
            }

            return JsonSerializer.Serialize(body);
        }

        private static TimeSpan RetryAfter(FileReply reply)
        {
            var wait = TimeSpan.FromSeconds(Math.Max(1, reply.RetryAfter ?? 1));

            return wait > MaxRetryAfter ? MaxRetryAfter : wait;
        }

        private static DateTime? ParseTime(string text) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time) ? time : (DateTime?)null;
    }
}
