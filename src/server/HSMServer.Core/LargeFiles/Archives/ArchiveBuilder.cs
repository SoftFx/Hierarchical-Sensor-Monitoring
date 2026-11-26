using HSMServer.Core.LargeFiles.DumpPasswords;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;

namespace HSMServer.Core.LargeFiles.Archives
{
    public sealed record ArchiveBuilderOptions
    {
        /// <summary>The delay after a step of the backoff list is used up.</summary>
        public static readonly TimeSpan AfterBackoff = TimeSpan.FromHours(1);


        /// <summary>Deflate level of the archives: 1 is the measured best trade of time against size for dumps.</summary>
        public int DeflateLevel { get; init; } = 1;

        /// <summary>The delay before the n-th retry of a failed build or of a build that had no room; then <see cref="AfterBackoff"/>.</summary>
        public TimeSpan[] Backoff { get; init; } =
        [
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(60),
        ];

        /// <summary>
        /// A pass builds from one listing of the store. A file verified or a password changed during the pass lists the
        /// store again once the listed work is done, or after this many builds, whichever comes first.
        /// </summary>
        public int BuildsPerListing { get; init; } = 64;
    }


    /// <summary>
    /// The archive builder of ADR-0022 points 12-13: one build at a time for the whole server. A first pack turns a verified
    /// part into an archive: under the product's current dump password for a secure file, without encryption and at once
    /// for a file that is not secure. A re-encryption rewrites a secure archive whose password version is older than the
    /// current one; an archive that is not secure is never rewritten and carries no version. Every archive is read back and
    /// checked against the SHA-256 of the original bytes before it is committed. No text this class writes (status, event,
    /// log) carries a password.
    /// </summary>
    public sealed class ArchiveBuilder : IArchiveBuilder, IDisposable
    {
        private const int CopyBufferSize = 1 << 20;

        private static readonly ArchiveBuildStatus _none = new(ArchiveBuildState.None, 0, null, null);

        private readonly ILargeFileArchiveStore _store;
        private readonly IArchiveWriter _writer;
        private readonly IDumpPasswordService _passwords;
        private readonly Func<DateTime> _utcNow;
        private readonly ArchiveBuilderOptions _options;
        private readonly ILogger _logger;

        private readonly SemaphoreSlim _run = new(1, 1);
        private readonly ConcurrentDictionary<Guid, Tracked> _files = new();
        private readonly ConcurrentDictionary<Guid, byte> _products = new();

        // The password versions the running build reads or writes: never retired while it runs.
        private readonly ConcurrentDictionary<(Guid Product, int Version), byte> _inFlight = new();

        // The packed archives whose password version cannot be used (not kept, or newer than the current one): each pair is
        // reported once per process and never retried by the builder itself.
        private readonly ConcurrentDictionary<(Guid FileId, int Version), byte> _unusable = new();

        // Set by a verified file or a password change: the running pass lists the store again (see RunDue).
        private int _changed;


        public ArchiveBuildStatistics Statistics { get; } = new();

        /// <summary>The files whose status is kept in memory: only those with work pending or a failure reported.</summary>
        internal int TrackedCount => _files.Count;

        public event Action<Guid, bool, string> BuildFinished;


        public ArchiveBuilder(ILargeFileArchiveStore store, IArchiveWriter writer, IDumpPasswordService passwords, Func<DateTime> utcNow,
                              ArchiveBuilderOptions options = null, ILogger logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _passwords = passwords ?? throw new ArgumentNullException(nameof(passwords));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _options = options ?? new ArchiveBuilderOptions();
            _logger = logger ?? LogManager.GetCurrentClassLogger();

            if (_options.DeflateLevel is < 0 or > 9)
                throw new ArgumentOutOfRangeException(nameof(options), "The deflate level must be 0..9.");

            if (_options.BuildsPerListing <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "The builds per listing must be positive.");

            _store.FileVerified += OnFileVerified;
            _passwords.PasswordChanged += OnPasswordChanged;
        }


        public ArchiveBuildStatus GetStatus(Guid fileId) => _files.TryGetValue(fileId, out var tracked) ? tracked.Status : _none;

        public async Task RunDueAsync(CancellationToken ct)
        {
            // A second caller waits for the running pass and then finds what is still due.
            await _run.WaitAsync(ct);
            try
            {
                await Task.Run(() => RunDue(ct), ct);
            }
            finally
            {
                _run.Release();
            }
        }

        public void Dispose()
        {
            _store.FileVerified -= OnFileVerified;
            _passwords.PasswordChanged -= OnPasswordChanged;
        }


        private void RunDue(CancellationToken ct)
        {
            // A file is not built twice for the same state in one pass, whatever the store answers.
            var started = new HashSet<WorkKey>();
            Queue<WorkItem> due = null;
            var builtSinceListing = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // One listing of the store per pass, not one per build. A file verified (or a password changed) while the
                // pass runs lists the store again once the listed work is done, or after BuildsPerListing builds, so it is
                // built in the same pass. Nothing is missed after a restart either: the events are only a hint.
                if (due is null || (Volatile.Read(ref _changed) != 0 && builtSinceListing > 0 &&
                                    (due.Count == 0 || builtSinceListing >= _options.BuildsPerListing)))
                {
                    Interlocked.Exchange(ref _changed, 0);
                    due = ListDue(started);
                    builtSinceListing = 0;
                }

                if (!due.TryDequeue(out var item))
                    break;

                started.Add(item.Key);
                Build(item, ct);
                builtSinceListing++;
            }

            // The removal checks again, against the same listing, that no packed archive carries a version it removes;
            // without a listing it waits for the next pass.
            if (RetireUnusedPasswordVersions() is { } carried)
                RemoveExpiredPasswordVersions(carried);
        }

        // The work of one listing, in the store's order: the first pack of a verified file (a secure one when its product
        // has a password, one that is not secure at once) and the re-encryption of a secure archive under an older version.
        // An archive that is not secure is never re-encrypted and carries no version, so it is not looked at.
        private Queue<WorkItem> ListDue(HashSet<WorkKey> started)
        {
            var now = _utcNow();
            var due = new List<WorkItem>();
            var pending = new HashSet<Guid>();
            var unusable = new HashSet<(Guid, int)>();

            foreach (var file in _store.GetFiles(LargeArchiveFileState.Verified))
            {
                _products.TryAdd(file.ProductId, 0);
                pending.Add(file.FileId);

                if (!file.IsSecure)
                {
                    due.Add(new WorkItem(file, BuildKind.FirstPack, 0));
                    continue;
                }

                var current = _passwords.GetCurrent(file.ProductId);
                if (current is null)
                    WaitForPassword(file.FileId, file.ProductId);
                else
                    due.Add(new WorkItem(file, BuildKind.FirstPack, current.Version));
            }

            foreach (var file in _store.GetFiles(LargeArchiveFileState.Packed))
            {
                if (!file.IsSecure)
                    continue;

                _products.TryAdd(file.ProductId, 0);

                // A product whose stored passwords cannot be read: its archives stay as they are, nothing is reported per
                // archive (the settings page and the log say the row cannot be read).
                if (_passwords.IsUnreadable(file.ProductId))
                    continue;

                var current = _passwords.GetCurrent(file.ProductId);

                if (CarriedVersion(file) is int carried && UnusableVersionError(file.ProductId, carried, current) is { } error)
                {
                    pending.Add(file.FileId);
                    unusable.Add((file.FileId, carried));
                    ReportUnusable(file.FileId, file.ProductId, carried, error);
                    continue;
                }

                if (current is null || CarriedVersion(file) >= current.Version)
                    continue;

                pending.Add(file.FileId);
                due.Add(new WorkItem(file, BuildKind.Reencryption, current.Version));
            }

            ForgetFinished(pending);

            // A (file, version) pair that is no longer listed as unusable is forgotten, so the set stays as small as the store.
            foreach (var pair in _unusable.Keys)
                if (!unusable.Contains(pair))
                    _unusable.TryRemove(pair, out _);

            var queue = new Queue<WorkItem>(due.Count);

            foreach (var item in due)
            {
                var status = _files.GetOrAdd(item.File.FileId, _ => new Tracked(item.File.ProductId, Status(ArchiveBuildState.Queued, 0, null, null))).Status;

                if (status.State == ArchiveBuildState.WaitingForPassword)
                    SetStatus(item.File.FileId, item.File.ProductId, Status(ArchiveBuildState.Queued, status.Attempts, null, null));
                else if (status.NextAttemptUtc > now)
                    continue;

                if (!started.Contains(item.Key))
                    queue.Enqueue(item);
            }

            return queue;
        }

        private void ForgetFinished(HashSet<Guid> pending)
        {
            foreach (var fileId in _files.Keys)
            {
                if (pending.Contains(fileId))
                    continue;

                try
                {
                    // A file verified after the listing above keeps the status its event gave it.
                    if (_store.TryGetFile(fileId)?.State != LargeArchiveFileState.Verified)
                        _files.TryRemove(fileId, out _);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Large file archives: the record of file {fileId} cannot be read: {StoreError(ex)}");
                }
            }
        }

        private void Build(WorkItem item, CancellationToken ct)
        {
            var fileId = item.File.FileId;
            var productId = item.File.ProductId;
            LargeArchiveTarget target = null;
            var inFlight = new List<(Guid, int)>(2);

            try
            {
                var record = _store.TryGetFile(fileId);
                if (!IsStillDue(record, item.Kind))
                {
                    Forget(fileId, "the file was removed or changed its state before the build");
                    return;
                }

                productId = record.ProductId;

                // A secure file takes the password (and its version) once: a change during the build is a re-encryption of
                // the next pass. A file that is not secure takes none and is never re-encrypted.
                DumpPasswordVersion password = null;
                if (record.IsSecure)
                {
                    password = _passwords.GetCurrent(productId);
                    if (password is null)
                    {
                        WaitForPassword(fileId, productId);
                        return;
                    }

                    if (item.Kind == BuildKind.Reencryption && CarriedVersion(record) >= password.Version)
                    {
                        Forget(fileId, "the archive is already under the current password");
                        return;
                    }
                }
                else if (item.Kind == BuildKind.Reencryption)
                {
                    Forget(fileId, "an archive that is not secure is never re-encrypted");
                    return;
                }

                if (record.ContentSha256 is not { Length: 32 })
                    throw new BuildException("the record has no SHA-256 of the file's content");

                DumpPasswordVersion source = null;
                if (item.Kind == BuildKind.FirstPack)
                {
                    var onDisk = _store.GetPartLengthOnDisk(fileId);
                    if (onDisk != record.PartLength)
                        throw new BuildException($"the part is {onDisk} bytes on the disk but the record covers {record.PartLength} bytes");
                }
                else
                {
                    if (CarriedVersion(record) is not int sourceVersion)
                        throw new BuildException("the packed record carries no dump password version");

                    source = _passwords.GetVersion(productId, sourceVersion);
                    if (source is null)
                    {
                        // Removed between the listing and the build.
                        ReportUnusable(fileId, productId, sourceVersion, NotKeptError(sourceVersion));
                        return;
                    }

                    inFlight.Add((productId, sourceVersion));
                }

                if (password is not null)
                    inFlight.Add((productId, password.Version));

                foreach (var version in inFlight)
                    _inFlight.TryAdd(version, 0);

                SetStatus(fileId, productId, Status(ArchiveBuildState.Building, GetStatus(fileId).Attempts, null, null));

                var begin = _store.BeginArchive(fileId);
                switch (begin.Outcome)
                {
                    case ArchiveBeginOutcome.Started:
                        target = begin.Target;
                        break;
                    case ArchiveBeginOutcome.NoRoom:
                        WaitForRoom(fileId, productId, begin.RetryAfter);
                        return;
                    case ArchiveBeginOutcome.NotFound:
                        Forget(fileId, "the store does not know the file");
                        return;
                    default:
                        Forget(fileId, $"the store refused the build ({begin.Outcome})");
                        return;
                }

                var expected = item.Kind == BuildKind.FirstPack ? null : record.ArchiveId;
                if (target.ExpectedArchiveId != expected)
                {
                    // Another archive was committed between the read of the record and the begin: look at the file again.
                    AbandonQuietly(ref target);
                    Requeue(fileId, productId, "the record changed before the build began");
                    return;
                }

                // A file that is not secure: no password, and the store keeps no version for it (0).
                var targetVersion = password?.Version ?? 0;

                var (archiveLength, archiveSha256) = WriteArchive(record, source, password?.Password, target.TempPath, ct);
                ReadBack(record, password?.Password, target.TempPath, ct);

                var committed = _store.CommitArchive(target, targetVersion, archiveLength, archiveSha256);
                target = null; // the store has dealt with the temporary file in every outcome

                switch (committed)
                {
                    case ArchiveCommitOutcome.Committed:
                        Succeed(fileId, productId, item.Kind, record.IsSecure, targetVersion, archiveLength);
                        break;
                    case ArchiveCommitOutcome.Superseded:
                        Requeue(fileId, productId, "another build committed first");
                        break;
                    default:
                        Forget(fileId, "the file was removed during the build");
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                AbandonQuietly(ref target);
                Requeue(fileId, productId, "the pass was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                AbandonQuietly(ref target);

                if (IsGone(fileId, item.Kind))
                    Forget(fileId, "the file was removed during the build");
                else if (ex is BuildException { OutOfSpace: true } || IsOutOfSpace(ex))
                    WaitForRoom(fileId, productId, null); // space taken outside HSM after the store's reservation
                else
                    Fail(fileId, productId, Describe(ex));
            }
            finally
            {
                // A build that began and did not reach the commit always gives its temporary archive and the store's
                // reservation back, whatever ended it.
                AbandonQuietly(ref target);

                foreach (var version in inFlight)
                    _inFlight.TryRemove(version, out _);
            }
        }

        // password: null for a file that is not secure, whose entry is not encrypted.
        private (long Length, byte[] Sha256) WriteArchive(LargeArchiveFile record, DumpPasswordVersion source, string password,
                                                         string tempPath, CancellationToken ct)
        {
            var entryName = ArchiveEntryNames.Sanitize(record.Name, record.Extension);

            using var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize);
            using var output = new HashingWriteStream(file);

            if (source is null)
            {
                using var part = _store.OpenPart(record.FileId);
                using var zstd = new DecompressionStream(part, CopyBufferSize, checkEndOfStream: true, leaveOpen: true);
                using var content = new CheckedReadStream(zstd, "reading the part", messageIsSafe: true, record.Size);

                WriteEntry(output, entryName, record, content, password, ct);
            }
            else
            {
                using var archive = _store.OpenArchive(record.FileId);
                using var entry = OpenEntry(archive, source.Password, "the current archive");
                using var content = new CheckedReadStream(entry.Content, "reading the current archive", messageIsSafe: false, record.Size);

                WriteEntry(output, entryName, record, content, password, ct);
            }

            output.Flush();
            file.Flush(flushToDisk: true);

            return (output.BytesWritten, output.GetHash());
        }

        private void WriteEntry(Stream output, string entryName, LargeArchiveFile record, Stream content, string password, CancellationToken ct)
        {
            try
            {
                _writer.Write(output, entryName, record.SourceModifiedUtc, record.Size, content, password, _options.DeflateLevel, ct);
            }
            catch (Exception ex) when (ex is not (BuildException or ArchiveFormatException or OperationCanceledException))
            {
                // The writer's own exceptions are not trusted to leave the password out of their text.
                throw new BuildException($"the archive writer failed ({ex.GetType().Name})", IsOutOfSpace(ex));
            }
        }

        // Opens the archive just written with the password it was written under (none for a file that is not secure) and
        // checks the decompressed content against the SHA-256 and the size of the original bytes: a broken archive never
        // replaces the part or the old archive.
        private void ReadBack(LargeArchiveFile record, string password, string tempPath, CancellationToken ct)
        {
            using var archive = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize);
            using var entry = OpenEntry(archive, password, "the new archive");
            using var content = new CheckedReadStream(entry.Content, "reading back the new archive", messageIsSafe: false, record.Size);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            var buffer = new byte[CopyBufferSize];
            int read;
            while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, read);
            }

            if (entry.Size != record.Size)
                throw new BuildException($"the entry of the new archive says {entry.Size} bytes but the file has {record.Size} bytes");

            if (!sha.GetHashAndReset().AsSpan().SequenceEqual(record.ContentSha256))
                throw new BuildException("the content of the new archive does not match the SHA-256 of the file");
        }

        private ArchiveEntryReader OpenEntry(Stream archive, string password, string what)
        {
            try
            {
                return _writer.OpenEntry(archive, password);
            }
            catch (ArchiveFormatException ex)
            {
                throw new BuildException($"{what} cannot be opened: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new BuildException($"{what} cannot be opened ({ex.GetType().Name})");
            }
        }


        // A version a re-encryption replaced is retired at the end of the pass, from one listing of the store.
        private void Succeed(Guid fileId, Guid productId, BuildKind kind, bool secure, int version, long archiveLength)
        {
            _files.TryRemove(fileId, out _);

            if (kind == BuildKind.FirstPack)
            {
                Interlocked.Increment(ref Statistics.Built);

                if (secure)
                    Interlocked.Increment(ref Statistics.BuiltSecure);
                else
                    Interlocked.Increment(ref Statistics.BuiltNotSecure);
            }
            else
                Interlocked.Increment(ref Statistics.Reencrypted);

            var how = secure ? $"under dump password version {version}" : "without encryption (not secure)";

            _logger.Info($"Large file archives: file {fileId} of product {productId} " +
                         $"{(kind == BuildKind.FirstPack ? "packed" : "re-encrypted")} {how} ({archiveLength} bytes)");

            RaiseBuildFinished(fileId, true, null);
        }

        private void Fail(Guid fileId, Guid productId, string error)
        {
            var attempts = GetStatus(fileId).Attempts + 1;
            var next = _utcNow() + Delay(attempts);

            SetStatus(fileId, productId, Status(ArchiveBuildState.Failed, attempts, next, error));
            Interlocked.Increment(ref Statistics.Failed);

            _logger.Error($"Large file archives: the build of file {fileId} of product {productId} failed (attempt {attempts}, next at {next:u}): {error}");

            RaiseBuildFinished(fileId, false, error);
        }

        // A packed archive cannot be re-encrypted when the version it carries is not kept (it cannot be opened), and must not
        // be when that version is newer than the current one; null when it can.
        private string UnusableVersionError(Guid productId, int version, DumpPasswordVersion current)
        {
            if (_passwords.GetVersion(productId, version) is null)
                return NotKeptError(version);

            if (current is not null && version > current.Version)
                return $"the password version {version} of this archive is newer than the current password version";

            return null;
        }

        private static string NotKeptError(int version) => $"the password version {version} of this archive is not kept";

        // Failed without a next attempt: retrying cannot help, and one report per (file, version) keeps it from being
        // repeated on every pass.
        private void ReportUnusable(Guid fileId, Guid productId, int version, string error)
        {
            var status = GetStatus(fileId);

            if (!_unusable.TryAdd((fileId, version), 0))
            {
                if (status.State != ArchiveBuildState.Failed || status.LastError != error || status.NextAttemptUtc is not null)
                    SetStatus(fileId, productId, Status(ArchiveBuildState.Failed, status.Attempts, null, error));

                return;
            }

            SetStatus(fileId, productId, Status(ArchiveBuildState.Failed, status.Attempts + 1, null, error));
            Interlocked.Increment(ref Statistics.Failed);

            _logger.Error($"Large file archives: the archive of file {fileId} of product {productId} cannot be re-encrypted, " +
                          $"not retried by itself: {error}");

            RaiseBuildFinished(fileId, false, error);
        }

        private void WaitForRoom(Guid fileId, Guid productId, TimeSpan? retryAfter)
        {
            const string error = "no room for the archive on the volume of the large files";

            var attempts = GetStatus(fileId).Attempts + 1;
            var next = _utcNow() + (retryAfter is { } after && after > TimeSpan.Zero ? after : Delay(attempts));

            SetStatus(fileId, productId, Status(ArchiveBuildState.WaitingForRoom, attempts, next, error));
            Interlocked.Increment(ref Statistics.WaitedForRoom);

            _logger.Warn($"Large file archives: file {fileId} of product {productId} waits for room (attempt {attempts}, next at {next:u})");

            RaiseBuildFinished(fileId, false, error);
        }

        private void WaitForPassword(Guid fileId, Guid productId)
        {
            var status = GetStatus(fileId);
            if (status.State != ArchiveBuildState.WaitingForPassword)
                SetStatus(fileId, productId, Status(ArchiveBuildState.WaitingForPassword, status.Attempts, null, null));
        }

        private void Requeue(Guid fileId, Guid productId, string reason)
        {
            SetStatus(fileId, productId, Status(ArchiveBuildState.Queued, GetStatus(fileId).Attempts, null, null));

            // The record changed under the build: the running pass lists the store again to see its new state.
            Interlocked.Exchange(ref _changed, 1);

            _logger.Info($"Large file archives: file {fileId} is looked at again: {reason}");
        }

        private void Forget(Guid fileId, string reason)
        {
            _files.TryRemove(fileId, out _);

            _logger.Info($"Large file archives: file {fileId} is not built: {reason}");
        }

        private TimeSpan Delay(int attempts)
        {
            var backoff = _options.Backoff ?? [];

            return attempts >= 1 && attempts <= backoff.Length ? backoff[attempts - 1] : ArchiveBuilderOptions.AfterBackoff;
        }

        private void SetStatus(Guid fileId, Guid productId, ArchiveBuildStatus status) => _files[fileId] = new Tracked(productId, status);

        private static ArchiveBuildStatus Status(ArchiveBuildState state, int attempts, DateTime? next, string error) => new(state, attempts, next, error);


        // A replaced version that no archive carries and no build reads or writes is retired once; the password service
        // keeps it readable until its retention ends and removes it in RemoveExpired. Returns the (product, version) pairs
        // the packed archives carry, or null when the store could not be listed.
        private HashSet<(Guid, int)> RetireUnusedPasswordVersions()
        {
            HashSet<(Guid, int)> carried = null;

            try
            {
                // Products with a password but no file seen in this process (after a restart) are retired too.
                foreach (var productId in _passwords.GetProductIds())
                    _products.TryAdd(productId, 0);

                // Only a secure archive carries a version: one that is not secure keeps no replaced password alive.
                var listed = new HashSet<(Guid, int)>();
                foreach (var file in _store.GetFiles(LargeArchiveFileState.Packed))
                {
                    _products.TryAdd(file.ProductId, 0);

                    if (CarriedVersion(file) is int version)
                        listed.Add((file.ProductId, version));
                }

                carried = listed;

                var now = _utcNow();
                foreach (var productId in _products.Keys)
                {
                    var current = _passwords.GetCurrent(productId);
                    if (current is null)
                        continue;

                    foreach (var version in _passwords.GetVersions(productId))
                    {
                        if (version.Version >= current.Version || version.IsRetired)
                            continue;

                        if (carried.Contains((productId, version.Version)) || _inFlight.ContainsKey((productId, version.Version)))
                            continue;

                        _passwords.MarkRetired(productId, version.Version, now);
                        Interlocked.Increment(ref Statistics.PasswordsRetired);

                        _logger.Info($"Large file archives: dump password version {version.Version} of product {productId} retired: no archive carries it");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Large file archives: the retirement of dump password versions failed: {StoreError(ex)}");
            }

            return carried;
        }

        private void RemoveExpiredPasswordVersions(IReadOnlySet<(Guid, int)> carried)
        {
            try
            {
                var removed = _passwords.RemoveExpired(_utcNow(), carried);
                if (removed > 0)
                    _logger.Info($"Large file archives: {removed} retired dump password version(s) removed after the retention");
            }
            catch (Exception ex)
            {
                _logger.Warn($"Large file archives: the removal of expired dump password versions failed: {StoreError(ex)}");
            }
        }


        private void OnFileVerified(LargeArchiveFile file)
        {
            try
            {
                if (file is null)
                    return;

                Interlocked.Exchange(ref _changed, 1);
                _products.TryAdd(file.ProductId, 0);

                // A file that is not secure never waits for a password.
                var state = !file.IsSecure || _passwords.HasPassword(file.ProductId) ? ArchiveBuildState.Queued : ArchiveBuildState.WaitingForPassword;
                _files.TryAdd(file.FileId, new Tracked(file.ProductId, Status(state, 0, null, null)));
            }
            catch (Exception ex)
            {
                _logger.Warn($"Large file archives: a verified file event failed: {StoreError(ex)}");
            }
        }

        private void OnPasswordChanged(Guid productId, int version)
        {
            Interlocked.Exchange(ref _changed, 1);
            _products.TryAdd(productId, 0);

            foreach (var (fileId, tracked) in _files)
                if (tracked.ProductId == productId && tracked.Status.State == ArchiveBuildState.WaitingForPassword)
                    _files.TryUpdate(fileId, tracked with { Status = Status(ArchiveBuildState.Queued, tracked.Status.Attempts, null, null) }, tracked);
        }

        private void RaiseBuildFinished(Guid fileId, bool ok, string error)
        {
            try
            {
                BuildFinished?.Invoke(fileId, ok, error);
            }
            catch (Exception ex)
            {
                _logger.Error($"Large file archives: a build finished handler failed ({ex.GetType().Name})");
            }
        }


        // The version a secure archive opens with; null for an archive that is not secure, and for 0 ("no version").
        private static int? CarriedVersion(LargeArchiveFile file) =>
            file.IsSecure && file.PasswordVersion is int version && version > 0 ? version : null;

        private bool IsStillDue(LargeArchiveFile record, BuildKind kind) =>
            record is not null && record.State == (kind == BuildKind.FirstPack ? LargeArchiveFileState.Verified : LargeArchiveFileState.Packed);

        private bool IsGone(Guid fileId, BuildKind kind)
        {
            try
            {
                return !IsStillDue(_store.TryGetFile(fileId), kind);
            }
            catch
            {
                return false;
            }
        }

        // Abandons once: the target is cleared, so a later call (the finally of the build) does nothing.
        private void AbandonQuietly(ref LargeArchiveTarget target)
        {
            if (target is null)
                return;

            var abandoned = target;
            target = null;
            AbandonQuietly(abandoned);
        }

        private void AbandonQuietly(LargeArchiveTarget target)
        {
            if (target is null)
                return;

            try
            {
                _store.AbandonArchive(target);
            }
            catch (Exception ex)
            {
                _logger.Error($"Large file archives: the temporary archive of file {target.FileId} cannot be abandoned: {StoreError(ex)}");
            }
        }

        // Texts the builder composes, and the archive format errors whose contract keeps the password out, are shown as
        // they are; any other exception comes from the store, the password service or the file system.
        private static string Describe(Exception ex) => ex switch
        {
            BuildException or ArchiveFormatException => ex.Message,
            _ => StoreError(ex),
        };

        private static string StoreError(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

        // A full volume: ERROR_HANDLE_DISK_FULL (0x27) or ERROR_DISK_FULL (0x70) as a Win32 HRESULT on Windows, the raw
        // errno ENOSPC (28) that .NET puts in an IOException's HResult on Linux.
        internal static bool IsOutOfSpace(Exception ex)
        {
            const int Win32Facility = unchecked((int)0x80070000);
            const int Enospc = 28;

            return ex is IOException { HResult: var hresult } &&
                   (hresult == Enospc || (hresult & unchecked((int)0xFFFF0000)) == Win32Facility && (hresult & 0xFFFF) is 0x27 or 0x70);
        }


        private enum BuildKind : byte
        {
            FirstPack,
            Reencryption,
        }


        private readonly record struct WorkKey(Guid FileId, BuildKind Kind, Guid? ArchiveId, int? PasswordVersion, int TargetVersion);


        private sealed record WorkItem(LargeArchiveFile File, BuildKind Kind, int TargetVersion)
        {
            public WorkKey Key => new(File.FileId, Kind, File.ArchiveId, File.PasswordVersion, TargetVersion);
        }


        private sealed record Tracked(Guid ProductId, ArchiveBuildStatus Status);


        /// <summary>A failure with a text the builder composed: it carries no password.</summary>
        private sealed class BuildException(string message, bool outOfSpace = false) : Exception(message)
        {
            /// <summary>The cause was a full volume: the build waits for room instead of failing.</summary>
            public bool OutOfSpace { get; } = outOfSpace;
        }


        /// <summary>
        /// The content of an archive being written or read back: exactly <c>expectedLength</c> bytes, and faults of the
        /// stream under it become <see cref="BuildException"/> with a text that says where it failed.
        /// </summary>
        private sealed class CheckedReadStream(Stream inner, string what, bool messageIsSafe, long expectedLength) : Stream
        {
            private long _read;


            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }


            public override int Read(byte[] buffer, int offset, int count)
            {
                int read;
                try
                {
                    read = inner.Read(buffer, offset, count);
                }
                catch (ArchiveFormatException ex)
                {
                    throw new BuildException($"{what} failed: {ex.Message}");
                }
                catch (Exception ex) when (ex is not (BuildException or OperationCanceledException))
                {
                    // A source the writer owns (an entry stream) may not be trusted to leave the password out of its text.
                    throw new BuildException(messageIsSafe ? $"{what} failed: {StoreError(ex)}" : $"{what} failed ({ex.GetType().Name})");
                }

                _read += read;

                if (_read > expectedLength)
                    throw new BuildException($"{what}: the content is longer than the {expectedLength} bytes of the file");

                if (read == 0 && count > 0 && _read != expectedLength)
                    throw new BuildException($"{what}: the content is {_read} bytes but the file has {expectedLength} bytes");

                return read;
            }

            public override void Flush() { }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }


        /// <summary>
        /// The forward-only output of the writer: hashes the archive bytes as they pass, so the archive is never read again
        /// for its SHA-256. Not seekable on purpose: a writer that patched a header would change bytes already hashed.
        /// </summary>
        private sealed class HashingWriteStream(Stream inner) : Stream
        {
            private readonly IncrementalHash _sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);


            public long BytesWritten { get; private set; }

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }


            public byte[] GetHash() => _sha.GetHashAndReset();

            public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                try
                {
                    inner.Write(buffer);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new BuildException($"writing the temporary archive failed: {StoreError(ex)}", IsOutOfSpace(ex));
                }

                _sha.AppendData(buffer);
                BytesWritten += buffer.Length;
            }

            public override void WriteByte(byte value) => Write([value]);

            public override void Flush()
            {
                try
                {
                    inner.Flush();
                }
                catch (Exception ex)
                {
                    throw new BuildException($"writing the temporary archive failed: {StoreError(ex)}", IsOutOfSpace(ex));
                }
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _sha.Dispose();

                base.Dispose(disposing);
            }
        }
    }
}
