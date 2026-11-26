using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.LargeFiles;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using NLog;

namespace HSMServer.Core.LargeFiles
{
    /// <summary>What the large file store is configured with (section <c>LargeFiles</c>).</summary>
    public sealed record LargeFileOptions
    {
        public long MaxFileSizeBytes { get; init; } = LargeFileStoreLimits.DefaultMaxFileSizeBytes;

        public int MaxUploadsInProgress { get; init; } = LargeFileStoreLimits.DefaultMaxUploadsInProgress;

        public long MinFreeDiskMb { get; init; } = LargeFileStoreLimits.DefaultMinFreeDiskMb;

        public int AbandonedUploadDays { get; init; } = LargeFileStoreLimits.DefaultAbandonedUploadDays;

        public int LeaseTimeoutSeconds { get; init; } = LargeFileStoreLimits.DefaultLeaseTimeoutSeconds;

        public int MaxUploadingPartsPerProduct { get; init; } = LargeFileStoreLimits.DefaultMaxUploadingPartsPerProduct;


        public LargeFileStoreLimits ToLimits() => new()
        {
            MaxFileSizeBytes = MaxFileSizeBytes,
            MaxUploadsInProgress = MaxUploadsInProgress,
            MinFreeBytes = MinFreeDiskMb * 1024 * 1024,
            AbandonedUploadAge = TimeSpan.FromDays(AbandonedUploadDays),
            LeaseTimeout = TimeSpan.FromSeconds(LeaseTimeoutSeconds),
            MaxUploadingPartsPerProduct = MaxUploadingPartsPerProduct,
        };
    }


    /// <summary>
    /// The beginning of an upload (#1883): the file and where it goes. <paramref name="SensorPath"/> is the normalized path
    /// a registration stores, looked up byte for byte in root product <paramref name="ProductId"/> (the product of the
    /// access key). <paramref name="Name"/> is shown and used as an archive entry name, never as a path on the server.
    /// <paramref name="SourceIdentity"/> is optional and opaque (a file id, an inode), at most 256 characters.
    /// <paramref name="ClientName"/> and <paramref name="RemoteAddress"/> say where the file came from (the session's client
    /// name and remote address): optional, at most 256 characters each, kept in the record of a new upload only.
    /// <paramref name="Secure"/> is the mark the application sets at the send (secure by default: a dump, its archive under
    /// the product's dump password; or not: the same zip without a password). It is part of the upload's key: the same file
    /// with the other mark is another upload. <paramref name="Status"/> and <paramref name="Comment"/> are what the value gets
    /// (a routed ordinary send keeps its own; Ok and none by default); the comment is sanitized as every value's comment and
    /// has at most 1,024 characters. All of them are kept in the record of a new upload; a continue keeps the record's.
    /// </summary>
    public sealed record LargeFileBeginRequest(
        Guid ProductId,
        string SensorPath,
        string Name,
        long Size,
        DateTime SourceModifiedUtc,
        string SourceIdentity,
        int PieceSize,
        int MaxPieceSize,
        string ClientName = null,
        string RemoteAddress = null,
        bool Secure = true,
        SensorStatus Status = SensorStatus.Ok,
        string Comment = null);


    /// <summary>
    /// The uploads of large files (ADR-0022, #1883): what the file connection (#1884) calls. Resolves the sensor, keeps the
    /// store's rules, and on a verified finish writes the value through the product's queue. Refusals are results; a fault
    /// of the store throws <see cref="LargeFileStoreException"/> (nothing was acknowledged). A caller's contract broken (a
    /// name missing or longer than 1,024 characters, a source identity longer than 256, a digest or hash that is not 32
    /// bytes, a stored piece outside its bounds) throws <see cref="ArgumentException"/>.
    /// </summary>
    public interface ILargeFileUploads
    {
        /// <summary>
        /// Begins or continues an upload. The name has 1..1,024 characters, the source identity, the client name and the
        /// remote address at most 256 each (<see cref="ArgumentException"/> otherwise); the piece size is within 65,536 .. <c>MaxPieceSize</c> (a refusal).
        /// </summary>
        Task<LargeFileBeginResult> BeginAsync(LargeFileBeginRequest request, CancellationToken ct);

        /// <summary>
        /// Appends a piece; returns once it is on the disk (the call blocks for the durable write). The digest has 32 bytes
        /// and the stored bytes 1 .. <c>LargeFileStore.MaxStoredPieceBytes(MaxPieceSize)</c> (the zstd bound of the maximum
        /// piece size); <see cref="ArgumentException"/> otherwise.
        /// </summary>
        Task<LargeFileAppendResult> AppendAsync(UploadLease lease, long pieceNumber, ReadOnlyMemory<byte> storedBytes, ReadOnlyMemory<byte> digest, CancellationToken ct);

        /// <summary>
        /// Removes the upload (part, index, record), releases its lease and reservation; logged and counted. False when
        /// nothing was removed: a stale lease (another connection holds the upload) or an unknown upload.
        /// </summary>
        Task<bool> DiscardAsync(UploadLease lease, string reason, CancellationToken ct);

        /// <summary>Ends a lease when the connection ends; idempotent; a stale lease is ignored.</summary>
        void Release(UploadLease lease);

        /// <summary>True while the end check of the file runs ("being checked").</summary>
        bool IsChecking(Guid fileId);

        /// <summary>
        /// Checks the stored pieces with <paramref name="checker"/> (<paramref name="wholeHash"/>, 32 bytes, is only its
        /// input) and, when they match, records the file as Verified on the disk with the checker's content hash and writes
        /// its value through the product's queue; answers Verified only after both. Runs on the caller's task for as long as
        /// the check takes; a Begin or a second Finish of the file meanwhile answers Checking. A cancelled or faulted check
        /// changes nothing on the disk. Once a check began, the finish ends the lease whatever its outcome.
        /// </summary>
        Task<LargeFileFinishResult> FinishAsync(UploadLease lease, byte[] wholeHash, ILargeFilePartChecker checker, CancellationToken ct);
    }


    /// <summary>What a pass of the large file store did (#1883), for the log and the tests.</summary>
    public sealed record LargeFilePassReport
    {
        public LargeFileStorePassReport Store { get; init; } = LargeFileStorePassReport.Empty;

        public int FilesRemoved { get; init; }

        public int PartsRemoved { get; init; }

        public int RolledForward { get; init; }

        public int RowDisagreements { get; init; }

        public int RowsWithoutRecord { get; init; }

        public int Unchecked { get; init; }
    }


    public sealed class LargeFileUploadService : ILargeFileUploads
    {
        public const int MaxNameLength = 1024;
        public const int MaxSourceIdentityLength = 256;
        public const int MaxOriginLength = 256;
        public const int MaxCommentLength = 1024;

        private static readonly Logger _logger = LogManager.GetLogger("LargeFiles");

        private readonly IDatabaseCore _database;
        private readonly TreeValuesCache _cache;
        private readonly SemaphoreSlim _passLock = new(1, 1);

        // Set once a check of the values against the store has completed without a failure (the start check, or a later pass
        // that repeats it): until then archives no record names are kept.
        private bool _valuesChecked;


        public LargeFileUploadService(IDatabaseCore database, ITreeValuesCache cache, LargeFileOptions options = null)
        {
            _database = database;
            _cache = cache as TreeValuesCache ?? throw new ArgumentException($"{nameof(LargeFileUploadService)} needs the server's {nameof(TreeValuesCache)}.", nameof(cache));

            // Nothing is opened or created here.
            var store = _database.LargeFiles ?? throw new ArgumentException("The database has no large file store.", nameof(database));
            store.Limits = (options ?? new LargeFileOptions()).ToLimits();
        }


        private ILargeFileStore Store => _database.LargeFiles;


        public Task<LargeFileBeginResult> BeginAsync(LargeFileBeginRequest request, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrEmpty(request.Name) || request.Name.Length > MaxNameLength)
                throw new ArgumentException($"A file name has 1..{MaxNameLength} characters.", nameof(request));

            if (request.SourceIdentity?.Length > MaxSourceIdentityLength)
                throw new ArgumentException($"A source identity has at most {MaxSourceIdentityLength} characters.", nameof(request));

            if (request.ClientName?.Length > MaxOriginLength || request.RemoteAddress?.Length > MaxOriginLength)
                throw new ArgumentException($"A client name and a remote address have at most {MaxOriginLength} characters.", nameof(request));

            if (request.Comment?.Length > MaxCommentLength)
                throw new ArgumentException($"A comment has at most {MaxCommentLength} characters.", nameof(request));

            if (!Enum.IsDefined(request.Status))
                throw new ArgumentException($"The status {request.Status} is not a sensor status.", nameof(request));

            ct.ThrowIfCancellationRequested();

            if (!TryResolveSensor(request.ProductId, request.SensorPath, out var sensor, out var error))
                return Task.FromResult(new LargeFileBeginResult(LargeFileBeginOutcome.SensorRefused, default, Guid.Empty, 0, 0, 0, null, error));

            var key = new LargeFileUploadKey(request.ProductId, sensor.Id, request.Name, request.Size, request.SourceModifiedUtc, request.SourceIdentity, request.Secure);
            var comment = request.Comment is null ? null : Services.HtmlSanitizerService.SanitizeText(request.Comment);

            return Task.FromResult(Store.Begin(key, ExtensionOf(request.Name), request.PieceSize, request.MaxPieceSize, request.ClientName, request.RemoteAddress, request.Status, comment));
        }

        public Task<LargeFileAppendResult> AppendAsync(UploadLease lease, long pieceNumber, ReadOnlyMemory<byte> storedBytes, ReadOnlyMemory<byte> digest, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            return Task.FromResult(Store.Append(lease, pieceNumber, storedBytes.Span, digest.Span));
        }

        public Task<bool> DiscardAsync(UploadLease lease, string reason, CancellationToken ct) => Task.FromResult(Store.Discard(lease, reason));

        public void Release(UploadLease lease) => Store.Release(lease);

        public bool IsChecking(Guid fileId) => Store.IsChecking(fileId);

        public async Task<LargeFileFinishResult> FinishAsync(UploadLease lease, byte[] wholeHash, ILargeFilePartChecker checker, CancellationToken ct)
        {
            if (wholeHash is null || wholeHash.Length != 32)
                throw new ArgumentException("The hash of a whole file has 32 bytes.", nameof(wholeHash));

            ArgumentNullException.ThrowIfNull(checker);

            var store = Store;
            var ticket = store.StartFinish(lease);
            var record = ticket.Record;

            switch (ticket.Outcome)
            {
                case LargeFileFinishStart.Incomplete:
                    return new LargeFileFinishResult(LargeFileFinishOutcome.Incomplete, lease.FileId, null, ticket.NextPiece, $"{ticket.NextPiece} of {record.TotalPieces} pieces are in.");
                case LargeFileFinishStart.Checking:
                    return new LargeFileFinishResult(LargeFileFinishOutcome.Checking, lease.FileId, null, ticket.NextPiece, "The end check of this file is running.");
                case LargeFileFinishStart.Discarded:
                    return new LargeFileFinishResult(LargeFileFinishOutcome.LeaseLost, lease.FileId, null, 0, "The upload is gone; begin again.");
                case LargeFileFinishStart.LeaseLost:
                    return new LargeFileFinishResult(LargeFileFinishOutcome.LeaseLost, lease.FileId, null, ticket.NextPiece, "The lease is lost; begin again.");
            }

            try
            {
                // The sensor before the check: a check of 64 GB for a sensor that is gone is not worth it.
                if (!SensorTakesRecord(record, out var why))
                {
                    store.Discard(lease, why);
                    return new LargeFileFinishResult(LargeFileFinishOutcome.SensorGone, record.FileId, null, ticket.NextPiece, why);
                }

                var check = await checker.CheckAsync(ticket.Reader, wholeHash, ct)
                            ?? throw new InvalidOperationException("The end check answered nothing.");

                if (!check.Matches)
                {
                    var defect = string.IsNullOrEmpty(check.Defect) ? "The stored file does not match the hash." : check.Defect;

                    store.Discard(lease, $"the end check failed{(check.FirstBadPiece is { } bad ? $" at piece {bad}" : string.Empty)}: {defect}");
                    return new LargeFileFinishResult(LargeFileFinishOutcome.Mismatch, record.FileId, null, 0, defect);
                }

                // What the record and the value keep is the hash of the original content, not the hash the collector sent.
                if (check.ContentHash is not { Length: 32 })
                    throw new InvalidOperationException("The end check matched without a 32-byte content hash.");

                var value = await _cache.AddLargeFileValueAsync(record, check.ContentHash, rollForward: false);

                switch (value.Outcome)
                {
                    case LargeFileValueOutcome.Added:
                        store.PublishVerified(record.FileId);
                        return new LargeFileFinishResult(LargeFileFinishOutcome.Verified, record.FileId, value.ValueTimeUtc, record.TotalPieces, null);

                    case LargeFileValueOutcome.SensorGone:
                        store.Discard(lease, value.Error);
                        return new LargeFileFinishResult(LargeFileFinishOutcome.SensorGone, record.FileId, null, ticket.NextPiece, value.Error);

                    default:
                        // The record may say Verified already: then the store's pass writes the row again from it, and a Begin of
                        // the file answers AlreadyStored. Either way nothing is acknowledged now. A fault of the store counted
                        // itself; the row's is counted here.
                        if (value.Exception is not LargeFileStoreException)
                            store.Statistics.Add(ref store.Statistics.WriteFailures);

                        _logger.Error(value.Exception, $"The value of large file {record.FileId:N} could not be written: {value.Error}");
                        throw new LargeFileStoreException($"The value of large file {record.FileId:N} could not be written: {value.Error}", value.Exception);
                }
            }
            finally
            {
                store.EndFinish(lease);
            }
        }


        /// <summary>
        /// The pass of the large file store (#1883): the store's own part (abandoned and orphan parts, leftovers), then the
        /// records against the sensors and their rows: a record of a removed sensor is removed; a verified or packed record
        /// whose row is gone below the sensor's history floor is removed, inside the kept range its row is written again; a
        /// row at its time that is not its value is reported. At <paramref name="atStart"/> also the rows of File sensors
        /// that hold a large value without a record are reported. Nothing here is repaired silently.
        /// </summary>
        public async Task<LargeFilePassReport> RunPassAsync(DateTime utcNow, bool atStart, CancellationToken token = default)
        {
            await _passLock.WaitAsync(token);

            try
            {
                return await RunPassCoreAsync(utcNow, atStart, token);
            }
            finally
            {
                _passLock.Release();
            }
        }

        private async Task<LargeFilePassReport> RunPassCoreAsync(DateTime utcNow, bool atStart, CancellationToken token)
        {
            var store = Store;
            var stats = store.Statistics;

            store.EnsureOpened();

            // Only on a server that ever took a large file: the folder alone may come from a bind mount. The mark that it did lives
            // with the databases, so a store folder that was lost or replaced by an empty one is still compared with the values.
            // The check runs at start and, until one has completed without a failure, at every later pass (a start pass that
            // stopped, a scan that was cancelled or could not read a sensor's rows).
            var rowsWithoutRecord = 0;

            if (atStart || !_valuesChecked)
            {
                if (store.HasContent || store.WasUsed)
                {
                    var (missing, failed) = await ReportRowsWithoutRecordAsync(token);
                    rowsWithoutRecord = missing;

                    // A value naming a file the store does not know, or rows that could not be read: an archive no record names
                    // may be that file.
                    if (missing > 0)
                        store.KeepOrphanArchives($"{missing} large values name files the store has no record of");

                    if (failed > 0)
                        store.KeepOrphanArchives($"the rows of {failed} File sensors could not be compared with the store");
                    else
                        _valuesChecked = true;
                }
                else
                {
                    // Never held a large file: no value can name one.
                    _valuesChecked = true;
                }

                if (_valuesChecked)
                    store.ConfirmValuesChecked();
            }

            if (!store.Exists)
                return new LargeFilePassReport { RowsWithoutRecord = rowsWithoutRecord };

            var storeReport = store.RunStorePass();
            int filesRemoved = 0, partsRemoved = 0, rolled = 0, disagreements = 0, notChecked = 0;

            var records = store.GetFiles();
            var alive = new List<LargeFileRecord>();

            // A sensor that is gone never comes back under its id: decided without the queue (its product may be gone too).
            // Gone means, by evidence, gone from the database: a read that succeeded did not find the key of its entity. A
            // sensor the tree did not build at start, an entity that cannot be read back, a removal that failed halfway, or a
            // read that failed: the files are kept (reported), since they may be the only copies. One read per sensor and pass.
            var stored = new Dictionary<Guid, bool?>();

            foreach (var record in records)
            {
                if (_cache.GetSensor(record.SensorId) is not null)
                {
                    alive.Add(record);
                    continue;
                }

                if (!IsGoneFromDatabase(record, stored))
                {
                    notChecked++;
                    continue;
                }

                if (store.Remove(record.FileId, "its sensor was removed"))
                {
                    if (record.State == LargeFileState.Uploading)
                        partsRemoved++;
                    else
                        filesRemoved++;
                }
            }

            foreach (var product in alive.GroupBy(r => r.ProductId))
            {
                token.ThrowIfCancellationRequested();

                var check = await _cache.CheckLargeFilesAsync(product.Key, [.. product], utcNow, token);

                if (check is null)
                {
                    notChecked += product.Count();
                    _logger.Warn($"The large files of product {product.Key} were not checked: its queue did not answer");
                    continue;
                }

                foreach (var verdict in check.Verdicts)
                {
                    var record = verdict.Record;

                    switch (verdict.Kind)
                    {
                        case LargeFileVerdictKind.SensorGone:
                            if (!IsGoneFromDatabase(record, stored))
                            {
                                notChecked++;
                                break;
                            }

                            if (store.Remove(record.FileId, verdict.Reason))
                            {
                                if (record.State == LargeFileState.Uploading)
                                    partsRemoved++;
                                else
                                    filesRemoved++;
                            }
                            break;

                        case LargeFileVerdictKind.ValueGone:
                            if (store.Remove(record.FileId, verdict.Reason))
                            {
                                if (record.State == LargeFileState.Uploading)
                                    partsRemoved++;
                                else
                                    filesRemoved++;
                            }
                            break;

                        case LargeFileVerdictKind.RolledForward:
                            rolled++;
                            stats.Add(ref stats.RollForwards);
                            _logger.Warn($"The value row of verified large file {record.FileId:N} was written again from its record: {verdict.Reason}");
                            break;

                        case LargeFileVerdictKind.RowDisagrees:
                        case LargeFileVerdictKind.Elsewhere:
                            disagreements++;
                            stats.Add(ref stats.RowDisagreements);
                            _logger.Warn($"Large file {record.FileId:N} ({record.State}) is kept, but {verdict.Reason}");
                            break;

                        case LargeFileVerdictKind.ReadFailed:
                            notChecked++;
                            _logger.Warn($"Large file {record.FileId:N} was not checked: {verdict.Reason}");
                            break;
                    }
                }
            }

            var report = new LargeFilePassReport
            {
                Store = storeReport,
                FilesRemoved = filesRemoved,
                PartsRemoved = partsRemoved,
                RolledForward = rolled,
                RowDisagreements = disagreements,
                RowsWithoutRecord = rowsWithoutRecord,
                Unchecked = notChecked,
            };

            if (report != new LargeFilePassReport { Store = storeReport } || storeReport != LargeFileStorePassReport.Empty)
                _logger.Info($"Large file pass: {filesRemoved} files and {partsRemoved + storeReport.AbandonedPartsRemoved + storeReport.OrphanPartsRemoved} parts removed, " +
                             $"{rolled} rows written again, {disagreements} rows that disagree, {rowsWithoutRecord} large values without a record, " +
                             $"{storeReport.OrphanPartsKept} parts without a record kept, {storeReport.LeftoversRemoved} leftovers removed, {notChecked} not checked");

            return report;
        }

        // True only on evidence that the record's sensor is gone: a read of the database that succeeded did not find the key of
        // its entity. Otherwise the file is kept, with a Warn line and a count. The answers are kept for the pass.
        private bool IsGoneFromDatabase(LargeFileRecord record, Dictionary<Guid, bool?> stored)
        {
            if (!stored.TryGetValue(record.SensorId, out var exists))
            {
                try
                {
                    exists = _database.SensorEntityExists(record.SensorId);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"The entity of sensor {record.SensorId} could not be read");
                    exists = null;
                }

                stored[record.SensorId] = exists;
            }

            if (exists == false)
                return true;

            var store = Store;
            store.Statistics.Add(ref store.Statistics.FilesOfUnloadedSensors);
            _logger.Warn($"Large file {record.FileId:N} ({record.State}) is kept: its sensor {record.SensorId} is not in the tree, " +
                         (exists is null ? "and the database could not tell whether it is still there" : "but its entity is still in the database (not loaded at start, unreadable, or a removal that failed)"));

            return false;
        }

        // The rows of File sensors that hold a large value whose file has no record. A row is recognised by its union tag
        // (its first byte); only those rows are deserialized.
        // The number of large values without a record, and of File sensors whose rows could not be read ("cannot tell").
        private async Task<(int Missing, int Failed)> ReportRowsWithoutRecordAsync(CancellationToken token)
        {
            var store = Store;
            var known = store.GetFiles().Select(r => r.FileId).ToHashSet();
            var missing = 0;
            var failed = 0;

            foreach (var sensor in _cache.GetSensors().OfType<FileSensorModel>())
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    await foreach (var row in _database.GetSensorValues(sensor.Id, DateTime.MinValue, DateTime.MaxValue).WithCancellation(token))
                    {
                        if (!LargeFileValue.IsLargeFileRow(row) || sensor.Convert(row) is not LargeFileValue value || known.Contains(value.FileId))
                            continue;

                        missing++;
                        store.Statistics.Add(ref store.Statistics.StartDisagreements);
                        _logger.Error($"The large value of sensor '{sensor.FullPath}' at {value.Time:O} names file {value.FileId:N}, which the large file store " +
                                      "does not have; the value is kept as it is and its file cannot be given out");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    store.Statistics.Add(ref store.Statistics.StartDisagreements);
                    _logger.Error(ex, $"The rows of sensor '{sensor.FullPath}' could not be compared with the large file store; archives no record names are kept");
                }
            }

            return (missing, failed);
        }


        private bool TryResolveSensor(Guid productId, string path, out BaseSensorModel sensor, out string error)
        {
            sensor = null;

            if (!_cache.TryGetRootProduct(productId, out _, out error))
                return false;

            // The normalized path a registration stored, byte for byte (as for table blocks): nothing is stored under a
            // path no read would find.
            if (string.IsNullOrEmpty(path) || !_cache.TryGetSensorFromCache(productId, path, out sensor))
            {
                error = $"No sensor is registered at '{path}' in product {productId}.";
                return false;
            }

            if (sensor is not FileSensorModel)
            {
                error = $"The sensor at '{path}' is a {sensor.Type} sensor, not a File sensor.";
                return false;
            }

            if (sensor.State == SensorState.Blocked)
            {
                error = $"The sensor at '{path}' is blocked.";
                return false;
            }

            return true;
        }

        private bool SensorTakesRecord(LargeFileRecord record, out string why)
        {
            var sensor = _cache.GetSensor(record.SensorId);

            why = sensor switch
            {
                null => $"sensor {record.SensorId} was removed",
                not FileSensorModel => $"sensor {record.SensorId} is not a File sensor",
                _ when sensor.Root.Id != record.ProductId => $"sensor '{sensor.FullPath}' was moved to another product",
                _ when sensor.State == SensorState.Blocked => $"sensor '{sensor.FullPath}' is blocked",
                _ => null,
            };

            return why is null;
        }

        private static string ExtensionOf(string name)
        {
            var extension = Path.GetExtension(name);

            return string.IsNullOrEmpty(extension) ? null : extension.TrimStart('.');
        }
    }
}
