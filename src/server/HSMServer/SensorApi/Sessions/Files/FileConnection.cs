using HSMSensorDataObjects;
using HSMServer.Middleware;
using NLog;
using System;
using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.SensorApi.Sessions.Files
{
    // The uploads of one file connection (#1884, ADR-0022 points 6-8): `begin` (text), pieces (binary),
    // `end` (text). Called by the handler loop of the connection only, one frame at a time; the end check
    // runs beside it.
    //
    // A connection holds at most one upload, that is one lease of the store. The lease goes back when the
    // connection ends, before another `begin`, when the upload is discarded, and after the end check,
    // which takes it over from the connection and gives it back whatever its outcome.
    internal sealed class FileConnection
    {
        // How long `end` waits for its check before it answers 202: a small file (a routed ordinary send, a
        // few pieces) gets its outcome in the first answer.
        public static readonly TimeSpan DefaultQuickCheck = TimeSpan.FromSeconds(2);

        public const int MaxPathLength = 1024;

        // The longest a comment of `begin` may be: a begin is a text frame, and a frame above 64 KiB needs an
        // upload the connection holds.
        public const int MaxCommentLength = 4096;

        // What a collector waits after a fault of the store, or a refusal that passes with time when the
        // store names no time.
        public const int FaultRetryAfterSeconds = 60;

        public const int MaxNameLength = 1024;
        public const int MaxSourceLength = 256;

        private readonly CollectorSession _session;
        private readonly IFileUploadStore _store;
        private readonly FileChecks _checks;
        private readonly int _maxPieceBytes;
        private readonly Logger _logger;
        private readonly TimeSpan _quickCheck;
        private readonly TimeProvider _time;

        // How long the connection counts on its lease without an append: the store's lease timeout, less a second for
        // the time between the store's touch and the answer that brings it here.
        private readonly TimeSpan _leaseLife;

        // The upload this connection holds; null when it holds none. Changed by the loop only.
        private volatile Upload _upload;

        // The last end check this connection started, kept to answer a repeated `end`.
        private CheckRun _check;


        public FileConnection(CollectorSession session, IFileUploadStore store, FileChecks checks, int maxPieceBytes, Logger logger, TimeSpan? quickCheck = null,
            TimeProvider time = null)
        {
            _session = session;
            _store = store;
            _checks = checks;
            _maxPieceBytes = maxPieceBytes;
            _logger = logger;
            _quickCheck = quickCheck ?? DefaultQuickCheck;
            _time = time ?? TimeProvider.System;
            _leaseLife = store.LeaseTimeout - TimeSpan.FromSeconds(1);
        }


        // True while the connection holds an upload whose lease is alive: only then may a frame be larger than 64 KiB. A
        // lease the store let expire (no append within its lease timeout) may be held by another connection now: the
        // connection forgets it, so its frames are bounded by the uploads in progress again.
        public bool HoldsUpload => Current() is not null;


        public async Task HandleBeginAsync(PublicApiRequestInfo caller, SessionRequest request, long id, ArrayBufferWriter<byte> output, CancellationToken token)
        {
            if (!TryReadBegin(request, out var begin, out var error))
            {
                Refuse(output, id, SessionProtocol.StatusBadRequest, error);
                return;
            }

            if (begin.PieceBytes is < FilePiece.MinPieceBytes || begin.PieceBytes > _maxPieceBytes)
            {
                Refuse(output, id, SessionProtocol.StatusBadRequest, $"invalid begin: pieceBytes must be {FilePiece.MinPieceBytes}..{_maxPieceBytes}");
                return;
            }

            // A connection holds one upload: the one it held goes back first.
            ReleaseUpload();

            FileBeginResult result;

            try
            {
                result = await _store.BeginAsync(new FileBeginRequest(caller.Product.Id, begin.Path, begin.Name, begin.Size, begin.ModifiedUtc, begin.Source,
                    begin.PieceBytes, _maxPieceBytes, begin.Secure, begin.Status, begin.Comment, _session.ClientName, _session.RemoteIP), token);
            }
            catch (ArgumentException ex)
            {
                Refuse(output, id, SessionProtocol.StatusBadRequest, $"invalid begin: {ex.Message}");
                return;
            }
            catch (FileUploadStoreException ex)
            {
                _logger.Debug($"Collector file connection {_session.ShortId}: the store faulted on a begin: {ex.Message}");
                Refuse(output, id, SessionProtocol.StatusUnavailable, "the large file store cannot take files now", FaultRetryAfterSeconds);
                return;
            }

            var upload = FormatId(result.FileId);

            switch (result.Outcome)
            {
                case FileBeginOutcome.Started or FileBeginOutcome.Continue:
                    var pieceSize = result.PieceSize > 0 ? result.PieceSize : begin.PieceBytes;
                    var pieces = FilePiece.PieceCount(begin.Size, pieceSize);

                    _upload = new Upload(result.Lease, result.FileId, begin.Size, pieceSize, pieces, result.NextPiece, CollectorSession.Printable(begin.Name),
                        _time.GetTimestamp());
                    _session.CountUploadBegun();

                    _logger.Info($"Collector file connection {_session.ShortId}: upload {upload} of \"{_upload.Name}\" ({begin.Size} bytes, {pieces} pieces of {pieceSize}) " +
                                 $"to \"{CollectorSession.Printable(begin.Path)}\" {(result.Outcome == FileBeginOutcome.Started ? "begun" : $"continued at piece {result.NextPiece}")}.");

                    SessionProtocol.WriteFileAck(output, id, SessionProtocol.StatusOk, body =>
                    {
                        body.WriteString("upload", upload);
                        body.WriteNumber("next", result.NextPiece);
                        body.WriteNumber("pieces", pieces);
                        body.WriteNumber("pieceBytes", pieceSize);
                    });
                    return;

                case FileBeginOutcome.AlreadyStored:
                    SessionProtocol.WriteFileAck(output, id, SessionProtocol.StatusOk, body =>
                    {
                        body.WriteString("upload", upload);
                        body.WriteString("state", "stored");
                    });
                    return;

                case FileBeginOutcome.Checking:
                    SessionProtocol.WriteFileAck(output, id, SessionProtocol.StatusAccepted, body =>
                    {
                        body.WriteString("upload", upload);
                        body.WriteString("state", "checking");
                    }, retryAfterSeconds: Seconds(result.RetryAfter, CheckRetryAfter(begin.Size)));
                    return;

                case FileBeginOutcome.NoRoom:
                    Refuse(output, id, SessionProtocol.StatusUnavailable, "no room for the file on the server's disk", Seconds(result.RetryAfter, FaultRetryAfterSeconds));
                    return;

                case FileBeginOutcome.TooManyUploads:
                    Refuse(output, id, SessionProtocol.StatusUnavailable, "too many uploads in progress", Seconds(result.RetryAfter, FaultRetryAfterSeconds));
                    return;

                case FileBeginOutcome.Busy:
                    Refuse(output, id, SessionProtocol.StatusConflict, "another connection is uploading this file", Seconds(result.RetryAfter, FaultRetryAfterSeconds));
                    return;

                case FileBeginOutcome.TooLarge:
                    Refuse(output, id, SessionProtocol.StatusTooLarge, $"the file is larger than the {_store.MaxFileBytes} bytes this server takes");
                    return;

                case FileBeginOutcome.TooManyParts:
                    Refuse(output, id, SessionProtocol.StatusUnavailable, "too many unfinished uploads of this product", Seconds(result.RetryAfter, FaultRetryAfterSeconds));
                    return;

                case FileBeginOutcome.BadPieceSize:
                    Refuse(output, id, SessionProtocol.StatusBadRequest, $"the piece size must be {FilePiece.MinPieceBytes}..{_maxPieceBytes}");
                    return;

                case FileBeginOutcome.SensorRefused:
                    Refuse(output, id, SessionProtocol.StatusUnprocessable, $"the sensor cannot take the file: {Short(result.Error)}");
                    return;

                default:
                    // An outcome this server does not know is never taken as final.
                    _logger.Error($"Collector file connection {_session.ShortId}: the store answered a begin with {result.Outcome}, which the connection does not know.");
                    Refuse(output, id, SessionProtocol.StatusUnavailable, "the large file store cannot take files now", FaultRetryAfterSeconds);
                    return;
            }
        }

        // A piece (kind 1). The checks go in the order of the contract: whose upload, which number, the
        // lengths in the header, the checksum, the zstd frame, then the store. A piece that breaks a rule of
        // its size discards the upload; a wrong checksum only refuses the piece.
        public async Task HandlePieceAsync(FilePiece header, ReadOnlyMemory<byte> frame, ArrayBufferWriter<byte> output, CancellationToken token)
        {
            var id = header.Id;
            var upload = Current();

            if (upload is null || header.UploadId != upload.FileId)
            {
                _session.CountPieceRefused();
                Refuse(output, id, SessionProtocol.StatusNotFound, "no such upload on this connection: begin again");
                return;
            }

            if (header.Number < 0 || header.Number >= upload.Pieces)
            {
                await DiscardAsync(upload, output, id, $"piece {header.Number} is outside 0..{upload.Pieces - 1}");
                return;
            }

            var length = FilePiece.LengthOf(header.Number, upload.Size, upload.PieceSize);
            var payload = frame[header.PayloadOffset..];

            if (header.DecompressedLength != length)
            {
                await DiscardAsync(upload, output, id, $"piece {header.Number} says {header.DecompressedLength} bytes, it has {length}");
                return;
            }

            if (header.CompressedLength != payload.Length || header.CompressedLength > FilePiece.CompressBound((int)length))
            {
                await DiscardAsync(upload, output, id, $"piece {header.Number} says {header.CompressedLength} compressed bytes, it brings {payload.Length} " +
                                                       $"and may have at most {FilePiece.CompressBound((int)length)}");
                return;
            }

            if (FilePiece.ComputeChecksum(payload.Span) != header.Checksum)
            {
                _session.CountPieceRefused();

                SessionProtocol.WriteFileAck(output, id, SessionProtocol.StatusConflict, body =>
                {
                    body.WriteNumber("next", header.Number);
                    body.WriteString("reason", "checksum mismatch");
                }, error: "the checksum of the piece does not match its bytes: send it again");
                return;
            }

            if (FilePiece.CheckFrame(payload.Span, length, FilePiece.MaxWindowLog(upload.PieceSize)) is { } frameError)
            {
                await DiscardAsync(upload, output, id, $"piece {header.Number}: {frameError}");
                return;
            }

            var digest = frame.Slice(56, FilePiece.DigestLength);
            FileAppendResult result;

            try
            {
                // Off the loop's thread: the store makes a synced write.
                result = await Task.Run(() => _store.AppendAsync(upload.Lease, header.Number, payload, digest, token), CancellationToken.None);
            }
            catch (FileUploadStoreException ex)
            {
                // Nothing was acknowledged: the collector sends the same piece again.
                _logger.Debug($"Collector file connection {_session.ShortId}: the store could not write piece {header.Number} of upload {FormatId(upload.FileId)}: {ex.Message}");
                _session.CountPieceRefused();
                Refuse(output, id, SessionProtocol.StatusUnavailable, "the large file store could not write the piece", FaultRetryAfterSeconds);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Error($"Failed to store piece {header.Number} of upload {FormatId(upload.FileId)} of collector file connection {_session.ShortId}: {ex}");
                _session.CountPieceRefused();
                Refuse(output, id, SessionProtocol.StatusInternalError, "failed to store the piece");
                return;
            }

            switch (result.Outcome)
            {
                case FileAppendOutcome.Stored:
                    _session.CountPieceStored(payload.Length);
                    upload.Next = result.NextPiece;
                    upload.Touched = _time.GetTimestamp();
                    Next(output, id, SessionProtocol.StatusOk, result.NextPiece, reason: null);
                    return;

                case FileAppendOutcome.AlreadyHave:
                    _session.CountPieceRepeated();
                    upload.Next = result.NextPiece;
                    Next(output, id, SessionProtocol.StatusOk, result.NextPiece, reason: null);
                    return;

                case FileAppendOutcome.OutOfOrder:
                    _session.CountPieceRefused();
                    upload.Next = result.NextPiece;
                    Next(output, id, SessionProtocol.StatusConflict, result.NextPiece, "out of order");
                    return;

                case FileAppendOutcome.TooMany:
                    await DiscardAsync(upload, output, id, $"piece {header.Number} is past the last piece");
                    return;

                default:
                    // The lease was lost or the upload removed: the collector begins again. Released here too, so an
                    // empty part goes at once.
                    _session.CountPieceRefused();
                    ForgetUpload(upload);
                    _store.Release(upload.Lease);
                    Refuse(output, id, SessionProtocol.StatusNotFound, "the upload is no longer held by this connection: begin again");
                    return;
            }
        }

        public async Task HandleEndAsync(SessionRequest request, long id, ArrayBufferWriter<byte> output)
        {
            if (!TryReadEnd(request, out var fileId, out var hash, out var error))
            {
                Refuse(output, id, SessionProtocol.StatusBadRequest, error);
                return;
            }

            var upload = Current();

            // A repeat of an end this connection already sent, with no new begin of that file since: answered
            // from its check.
            if ((upload is null || upload.FileId != fileId) && _check is { } check && check.FileId == fileId)
            {
                AnswerCheck(check, output, id);
                return;
            }

            if (upload is null || upload.FileId != fileId)
            {
                Refuse(output, id, SessionProtocol.StatusNotFound, "no such upload on this connection: begin again");
                return;
            }

            if (upload.Next < upload.Pieces)
            {
                Next(output, id, SessionProtocol.StatusConflict, upload.Next, "pieces are missing");
                return;
            }

            // The check takes the lease over and gives it back whatever its outcome; the connection holds no
            // upload from here on.
            _upload = null;
            _check = StartCheck(upload, hash);

            // A quick check is answered with its outcome; a long one with 202, and the collector asks again.
            // Not past the start of a shutdown, which waits for this frame's answer before it closes.
            try
            {
                await _check.Task.WaitAsync(_quickCheck, _checks.Closing);
            }
            catch (Exception)
            {
                // Past the wait, cut by a shutdown, or a faulted check: AnswerCheck tells which.
            }

            AnswerCheck(_check, output, id);
        }

        // How long a collector waits before it asks again about a running check: about the time a check reads
        // a file of this size (256 MiB a second), 1..30 seconds.
        internal static int CheckRetryAfter(long size) => (int)Math.Clamp((size + (256L << 20) - 1) / (256L << 20), 1, 30);

        // `discard` (ADR-0022 point 3): the collector gives up the upload this connection holds, because its
        // source file changed while it was sent. The part, its reservation and its slot go; the collector then
        // begins the changed file as a new one. A store fault changes nothing and keeps the upload held, so the
        // same request can be sent again.
        public async Task HandleDiscardAsync(SessionRequest request, long id, ArrayBufferWriter<byte> output)
        {
            if (!request.HasBody || request.Body.ValueKind != JsonValueKind.Object)
            {
                Refuse(output, id, SessionProtocol.StatusBadRequest, "malformed body");
                return;
            }

            if (!TryString(request.Body, "upload", out var text) || !FilePiece.TryParseUploadId(text, out var fileId))
            {
                Refuse(output, id, SessionProtocol.StatusBadRequest, "invalid discard: upload must be 32 hex digits");
                return;
            }

            var upload = Current();

            if (upload is null || upload.FileId != fileId)
            {
                // After an end the check holds the upload: it settles it, and a begin then tells the result.
                if (_check is { } check && check.FileId == fileId && !check.Task.IsCompleted)
                {
                    SessionProtocol.WriteFileAck(output, id, SessionProtocol.StatusConflict, body =>
                    {
                        body.WriteString("upload", FormatId(fileId));
                        body.WriteString("state", "checking");
                    }, error: "the end check of this upload is running", retryAfterSeconds: check.RetryAfterSeconds);
                    return;
                }

                Refuse(output, id, SessionProtocol.StatusNotFound, "no such upload on this connection: begin it first");
                return;
            }

            const string reason = "the collector discarded it: its source file changed";

            bool removed;

            try
            {
                removed = await _store.DiscardAsync(upload.Lease, reason, CancellationToken.None);
            }
            catch (Exception ex) when (ex is FileUploadStoreException or OperationCanceledException)
            {
                _logger.Debug($"Collector file connection {_session.ShortId}: the store could not discard upload {FormatId(upload.FileId)}: {ex.Message}");
                Refuse(output, id, SessionProtocol.StatusUnavailable, "the large file store could not discard the upload", FaultRetryAfterSeconds);
                return;
            }

            ForgetUpload(upload);

            // A discard ends the lease; Release is idempotent.
            _store.Release(upload.Lease);

            if (!removed)
            {
                // Its lease was stale: another connection holds the upload now, and nothing was removed.
                Refuse(output, id, SessionProtocol.StatusNotFound, "the upload is no longer held by this connection: begin it first");
                return;
            }

            _session.CountUploadDiscarded();
            _logger.Info($"Collector file connection {_session.ShortId}: upload {FormatId(upload.FileId)} of \"{upload.Name}\" is discarded: {reason}.");

            SessionProtocol.WriteFileAck(output, id, SessionProtocol.StatusOk, body =>
            {
                body.WriteString("upload", FormatId(fileId));
                body.WriteString("state", "discarded");
            });
        }

        // The connection has ended: its lease goes back, unless a running check holds it.
        public void End() => ReleaseUpload();


        private CheckRun StartCheck(Upload upload, byte[] hash)
        {
            var run = new CheckRun(upload.FileId, upload.Name, CheckRetryAfter(upload.Size));

            run.Task = _checks.Start(async stopping =>
            {
                var checker = new FilePieceChecker();

                try
                {
                    var result = await _store.FinishAsync(upload.Lease, hash, checker, stopping);

                    // The checker's own words for what was wrong, whatever text the store gives its mismatch.
                    if (result.Outcome == FileFinishOutcome.Mismatch && checker.Last is { Matches: false, Defect: { } defect })
                        result = result with { Error = defect };

                    Report(upload, result);
                    return CheckAnswer.From(result);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    return CheckAnswer.Fault("the server is shutting down");
                }
                catch (FileUploadStoreException ex)
                {
                    _logger.Warn($"The end check of upload {FormatId(upload.FileId)} (\"{upload.Name}\") of collector file connection {_session.ShortId} could not read the file: {ex.Message}");
                    return CheckAnswer.Fault("the large file store could not read the file");
                }
                catch (Exception ex)
                {
                    _logger.Error($"The end check of upload {FormatId(upload.FileId)} of collector file connection {_session.ShortId} failed: {ex}");
                    return CheckAnswer.Fault("the end check failed");
                }
                finally
                {
                    _store.Release(upload.Lease);
                }
            });

            return run;
        }

        private void Report(Upload upload, FileFinishResult result)
        {
            var uploadId = FormatId(upload.FileId);

            switch (result.Outcome)
            {
                case FileFinishOutcome.Verified:
                    _session.CountUploadStored();
                    _logger.Info($"Collector file connection {_session.ShortId}: upload {uploadId} of \"{upload.Name}\" ({upload.Size} bytes) is stored.");
                    break;

                case FileFinishOutcome.Mismatch:
                    _session.CountUploadDiscarded();
                    _logger.Warn($"Collector file connection {_session.ShortId}: upload {uploadId} of \"{upload.Name}\" is discarded, its end check failed: {Short(result.Error)}");
                    break;

                case FileFinishOutcome.SensorGone:
                    _session.CountUploadDiscarded();
                    _logger.Info($"Collector file connection {_session.ShortId}: upload {uploadId} of \"{upload.Name}\" is discarded, its sensor cannot take it: {Short(result.Error)}");
                    break;
            }
        }

        private void AnswerCheck(CheckRun check, ArrayBufferWriter<byte> output, long id)
        {
            var upload = FormatId(check.FileId);

            if (!check.Task.IsCompleted)
            {
                SessionProtocol.WriteFileAck(output, id, SessionProtocol.StatusAccepted, body =>
                {
                    body.WriteString("upload", upload);
                    body.WriteString("state", "checking");
                }, retryAfterSeconds: check.RetryAfterSeconds);
                return;
            }

            // The check catches what it meets; a throw past it (its Release) is a fault of the server.
            var answer = check.Task.IsCompletedSuccessfully ? check.Task.Result : CheckAnswer.Fault("the end check failed");

            switch (answer.Kind)
            {
                case CheckAnswerKind.Stored:
                    SessionProtocol.WriteFileAck(output, id, SessionProtocol.StatusOk, body =>
                    {
                        body.WriteString("upload", upload);
                        body.WriteString("state", "stored");

                        if (answer.ValueTimeUtc is { } time)
                            body.WriteString("time", time.ToString("O", CultureInfo.InvariantCulture));
                    });
                    return;

                case CheckAnswerKind.Checking:
                    SessionProtocol.WriteFileAck(output, id, SessionProtocol.StatusAccepted, body =>
                    {
                        body.WriteString("upload", upload);
                        body.WriteString("state", "checking");
                    }, retryAfterSeconds: check.RetryAfterSeconds);
                    return;

                case CheckAnswerKind.Missing:
                    Next(output, id, SessionProtocol.StatusConflict, answer.NextPiece, "pieces are missing");
                    return;

                case CheckAnswerKind.Mismatch:
                    Refuse(output, id, SessionProtocol.StatusBadRequest, $"{answer.Error}: the upload is discarded");
                    return;

                case CheckAnswerKind.SensorGone:
                    Refuse(output, id, SessionProtocol.StatusUnprocessable, $"the sensor cannot take the file: {answer.Error}");
                    return;

                case CheckAnswerKind.Lost:
                    Refuse(output, id, SessionProtocol.StatusNotFound, "the upload is no longer held: begin again");
                    return;

                default:
                    Refuse(output, id, SessionProtocol.StatusUnavailable, $"{answer.Error}: begin again", answer.RetryAfterSeconds);
                    return;
            }
        }

        private async Task DiscardAsync(Upload upload, ArrayBufferWriter<byte> output, long id, string reason)
        {
            _session.CountPieceRefused();
            ForgetUpload(upload);

            try
            {
                // Not cut short by the connection's end: a discard is short, and a lease it left would hold an
                // upload slot and make the file busy until the store's lease timeout.
                await _store.DiscardAsync(upload.Lease, reason, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // The part stays until the store's own pass; the answer does not change.
                _logger.Error($"Failed to discard upload {FormatId(upload.FileId)} of collector file connection {_session.ShortId}: {ex}");
            }
            finally
            {
                // A discard ends the lease; this is for the discard that failed (Release is idempotent).
                _store.Release(upload.Lease);
            }

            _session.CountUploadDiscarded();
            _logger.Info($"Collector file connection {_session.ShortId}: upload {FormatId(upload.FileId)} of \"{upload.Name}\" is discarded: {reason}.");

            Refuse(output, id, SessionProtocol.StatusBadRequest, $"{reason}: the upload is discarded");
        }

        // The upload this connection holds, while its lease is alive; one whose lease expired is forgotten and given back.
        private Upload Current()
        {
            var upload = _upload;

            if (upload is null || _time.GetElapsedTime(upload.Touched) < _leaseLife)
                return upload;

            ForgetUpload(upload);
            _store.Release(upload.Lease);

            _logger.Debug($"Collector file connection {_session.ShortId}: the lease of upload {FormatId(upload.FileId)} expired; the connection no longer holds it.");

            return null;
        }

        private void ReleaseUpload()
        {
            var upload = _upload;

            if (upload is null)
                return;

            _upload = null;
            _store.Release(upload.Lease);
        }

        private void ForgetUpload(Upload upload)
        {
            if (ReferenceEquals(_upload, upload))
                _upload = null;
        }

        private void Refuse(ArrayBufferWriter<byte> output, long id, int status, string error, int? retryAfterSeconds = null)
        {
            _session.CountRequestRefused();
            SessionProtocol.WriteFileAck(output, id, status, body: null, error, retryAfterSeconds);
        }

        private static void Next(ArrayBufferWriter<byte> output, long id, int status, long next, string reason) =>
            SessionProtocol.WriteFileAck(output, id, status, body =>
            {
                body.WriteNumber("next", next);

                if (reason is not null)
                    body.WriteString("reason", reason);
            });

        // Whole seconds, rounded up, at least 1.
        internal static int Seconds(TimeSpan? wait, int otherwise) =>
            wait is { } value && value > TimeSpan.Zero ? (int)Math.Max(1, Math.Ceiling(Math.Min(value.TotalSeconds, int.MaxValue))) : otherwise;

        private static string FormatId(Guid id) => FilePiece.FormatUploadId(id);

        private static string Short(string text) => CollectorSession.Printable(text ?? "no reason given", 200);


        private static bool TryReadBegin(SessionRequest request, out Begin begin, out string error)
        {
            begin = default;

            if (!request.HasBody || request.Body.ValueKind != JsonValueKind.Object)
            {
                error = "malformed body";
                return false;
            }

            var body = request.Body;

            if (!TryString(body, "path", out var path) || string.IsNullOrEmpty(path) || path.Length > MaxPathLength)
            {
                error = $"invalid begin: path must have 1..{MaxPathLength} characters";
                return false;
            }

            if (!TryString(body, "name", out var name) || string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            {
                error = $"invalid begin: name must have 1..{MaxNameLength} characters";
                return false;
            }

            if (!body.TryGetProperty("size", out var sizeElement) || sizeElement.ValueKind != JsonValueKind.Number || !sizeElement.TryGetInt64(out var size) || size < 0)
            {
                error = "invalid begin: size must be a whole number of bytes, 0 or more";
                return false;
            }

            if (!TryString(body, "modified", out var modifiedText) || !TryReadUtc(modifiedText, out var modified))
            {
                error = "invalid begin: modified must be a time with a UTC designator or an offset";
                return false;
            }

            string source = null;

            if (body.TryGetProperty("source", out var sourceElement) && sourceElement.ValueKind != JsonValueKind.Null)
            {
                if (sourceElement.ValueKind != JsonValueKind.String || (source = sourceElement.GetString()).Length is 0 or > MaxSourceLength)
                {
                    error = $"invalid begin: source must have 1..{MaxSourceLength} characters";
                    return false;
                }
            }

            if (!body.TryGetProperty("pieceBytes", out var pieceElement) || pieceElement.ValueKind != JsonValueKind.Number || !pieceElement.TryGetInt32(out var pieceBytes))
            {
                error = "invalid begin: pieceBytes is missing";
                return false;
            }

            // Absent means secure: an older collector, or a mistake, fails closed (ADR-0022 point 20).
            var secure = true;

            if (body.TryGetProperty("secure", out var secureElement) && secureElement.ValueKind != JsonValueKind.Null)
            {
                if (secureElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    error = "invalid begin: secure must be true or false";
                    return false;
                }

                secure = secureElement.GetBoolean();
            }

            // The status of a routed ordinary send, as the Status of a value in a list frame; absent means Ok.
            var status = SensorStatus.Ok;

            if (body.TryGetProperty("status", out var statusElement) && statusElement.ValueKind != JsonValueKind.Null)
            {
                if (statusElement.ValueKind != JsonValueKind.Number || !statusElement.TryGetInt32(out var statusNumber) || !Enum.IsDefined(typeof(SensorStatus), statusNumber))
                {
                    error = "invalid begin: status must be a SensorStatus number";
                    return false;
                }

                status = (SensorStatus)statusNumber;
            }

            string comment = null;

            if (body.TryGetProperty("comment", out var commentElement) && commentElement.ValueKind != JsonValueKind.Null)
            {
                if (commentElement.ValueKind != JsonValueKind.String || (comment = commentElement.GetString()).Length > MaxCommentLength)
                {
                    error = $"invalid begin: comment must be text of at most {MaxCommentLength} characters";
                    return false;
                }
            }

            begin = new Begin(path, name, size, modified, source, pieceBytes, secure, status, comment);
            error = null;
            return true;
        }

        private static bool TryReadEnd(SessionRequest request, out Guid fileId, out byte[] hash, out string error)
        {
            fileId = Guid.Empty;
            hash = null;

            if (!request.HasBody || request.Body.ValueKind != JsonValueKind.Object)
            {
                error = "malformed body";
                return false;
            }

            if (!TryString(request.Body, "upload", out var upload) || !FilePiece.TryParseUploadId(upload, out fileId))
            {
                error = "invalid end: upload must be 32 hex digits";
                return false;
            }

            if (!TryString(request.Body, "hash", out var hashText) || hashText.Length != 64 || !TryHex(hashText, out hash))
            {
                error = "invalid end: hash must be 64 hex digits";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryString(JsonElement body, string name, out string value)
        {
            value = body.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

            return value is not null;
        }

        // A time that says it is UTC ("Z") or names its offset; a time without either is not taken.
        private static bool TryReadUtc(string text, out DateTime utc)
        {
            utc = default;

            if (text.Length < 20 || !(text.EndsWith('Z') || text.EndsWith("z") || text[^6] is '+' or '-'))
                return false;

            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return false;

            utc = parsed.UtcDateTime;
            return true;
        }

        private static bool TryHex(string text, out byte[] bytes)
        {
            try
            {
                bytes = Convert.FromHexString(text);
                return true;
            }
            catch (FormatException)
            {
                bytes = null;
                return false;
            }
        }


        private readonly record struct Begin(string Path, string Name, long Size, DateTime ModifiedUtc, string Source, int PieceBytes, bool Secure,
            SensorStatus Status, string Comment);


        private sealed class Upload(FileUploadLease lease, Guid fileId, long size, int pieceSize, long pieces, long next, string name, long touched)
        {
            // When the store last renewed the lease, as this connection saw it: the begin, a stored piece.
            public long Touched { get; set; } = touched;

            public FileUploadLease Lease { get; } = lease;

            public Guid FileId { get; } = fileId;

            public long Size { get; } = size;

            public int PieceSize { get; } = pieceSize;

            public long Pieces { get; } = pieces;

            public string Name { get; } = name;

            // The piece the store wants next, as its last answer said.
            public long Next { get; set; } = next;
        }


        private sealed class CheckRun(Guid fileId, string name, int retryAfterSeconds)
        {
            public Guid FileId { get; } = fileId;

            public string Name { get; } = name;

            public int RetryAfterSeconds { get; } = retryAfterSeconds;

            public Task<CheckAnswer> Task { get; set; }
        }


        private enum CheckAnswerKind
        {
            Stored,
            Checking,
            Missing,
            Mismatch,
            SensorGone,
            Lost,
            Fault,
        }


        private sealed record CheckAnswer(CheckAnswerKind Kind, DateTime? ValueTimeUtc, long NextPiece, string Error, int RetryAfterSeconds = FaultRetryAfterSeconds)
        {
            public static CheckAnswer Fault(string error) => new(CheckAnswerKind.Fault, null, 0, error);

            // A finish that found the file being checked already (by another connection) is over for this one: a
            // repeat of its answer must not say "checking" for ever. The collector asks the store again by begin.
            public static CheckAnswer From(FileFinishResult result) => result.Outcome switch
            {
                FileFinishOutcome.Verified => new(CheckAnswerKind.Stored, result.ValueTimeUtc, 0, null),
                FileFinishOutcome.Checking => new(CheckAnswerKind.Fault, null, 0, "the end check of this file runs elsewhere", 5),
                FileFinishOutcome.Incomplete => new(CheckAnswerKind.Missing, null, result.NextPiece, null),
                FileFinishOutcome.Mismatch => new(CheckAnswerKind.Mismatch, null, 0, Short(result.Error)),
                FileFinishOutcome.SensorGone => new(CheckAnswerKind.SensorGone, null, 0, Short(result.Error)),
                _ => new(CheckAnswerKind.Lost, null, 0, null),
            };
        }
    }
}
