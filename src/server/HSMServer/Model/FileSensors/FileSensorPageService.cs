using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Extensions;
using HSMCommon.Model;
using HSMServer.Core.Cache;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model;
using HSMServer.Model.Authentication;

namespace HSMServer.Model.FileSensors
{
    /// <summary>
    /// The file values of a File sensor for its page (#1886): the description of the last value, one value by its time, and
    /// the list of values page by page. Reads the sensor's history directly, not through the user's History tab state, so
    /// the two never share a table. The caller has checked that the user may see the sensor's product.
    /// </summary>
    public sealed class FileSensorPageService
    {
        /// <summary>Values per page of the list.</summary>
        public const int PageSize = 20;

        /// <summary>The downloads of one file the page lists.</summary>
        public const int MaxDownloadsShown = 20;

        private const long CsvPreviewLimit = 5 * 1024 * 1024;
        private const long TxtPreviewLimit = 1024 * 1024;

        private readonly ITreeValuesCache _cache;
        private readonly ILargeFilePageSource _largeFiles;
        private readonly NLog.Logger _logger = NLog.LogManager.GetLogger(nameof(FileSensorPageService));


        internal FileSensorPageService(ITreeValuesCache cache) : this(cache, new NoLargeFilePageSource()) { }

        public FileSensorPageService(ITreeValuesCache cache, ILargeFilePageSource largeFiles)
        {
            _cache = cache;
            _largeFiles = largeFiles ?? new NoLargeFilePageSource();
        }


        internal static bool IsLarge(FileValue file) => file.IsLargeFile();

        /// <summary>A text preview for an ordinary csv or txt file small enough to show; never for a large value.</summary>
        internal static bool IsPreviewEnabled(FileValue file) =>
            !IsLarge(file) &&
            ((file.Extension == "csv" && file.OriginalSize < CsvPreviewLimit) ||
             (file.Extension == "txt" && file.OriginalSize < TxtPreviewLimit));


        public FileInfoDto GetInfo(Guid sensorId) => FileInfoDto.From(GetLastValue(sensorId));

        /// <summary>
        /// The value at <paramref name="timeTicks"/> (the key of its row), read by that time alone; the last value when it is
        /// 0. Null when there is none; <see cref="FileRead.Busy"/> when the sensor's history was being read by another reader,
        /// which is not "none": ask again. The content is decompressed for an ordinary file value.
        /// </summary>
        public async Task<FileRead> GetFileAsync(Guid sensorId, long timeTicks)
        {
            if (timeTicks == default)
                return new FileRead(GetLastValue(sensorId)?.DecompressContent(), false);

            if (timeTicks < DateTime.MinValue.Ticks || timeTicks > DateTime.MaxValue.Ticks)
                return new FileRead(null, false);

            // Never a range of one tick: the weekly databases are chosen by a strict overlap, so [t, t] misses a value at
            // the very first tick of a week. One tick more, and the value is picked by its time.
            var from = new DateTime(timeTicks, DateTimeKind.Utc);
            var to = new DateTime(Math.Min(timeTicks + 1, DateTime.MaxValue.Ticks), DateTimeKind.Utc);
            var (values, busy) = await ReadAsync(sensorId, from, to, 2);

            return new FileRead(values.OfType<FileValue>().FirstOrDefault(file => file.Time.Ticks == timeTicks)?.DecompressContent(), busy);
        }

        // The history of one File sensor is read by one reader at a time; the cache answers a refused read with an empty
        // page. An empty page while the lock is held (before or after this read) is a refusal, not the absence of values.
        private async Task<(List<BaseValue> Values, bool Busy)> ReadAsync(Guid sensorId, DateTime from, DateTime to, int count)
        {
            var heldBefore = _cache.IsFileHistoryReadInProgress(sensorId);
            var values = await _cache.GetSensorValuesPage(sensorId, from, to, count).Flatten();
            var busy = values.Count == 0 && (heldBefore || _cache.IsFileHistoryReadInProgress(sensorId));

            return (values, busy);
        }

        /// <summary>
        /// A page of the sensor's file values, newest first: the newest <see cref="PageSize"/> without an ordinary last value
        /// (the page shows it on its own above the list), or the <see cref="PageSize"/> values older than
        /// <paramref name="olderThanTicks"/>. No limit on how far back the pages go. The first page carries the uploads in
        /// progress; a large value carries its state, its download and, for a Manager of the product or an admin, its
        /// downloads.
        /// </summary>
        public async Task<FileValuesPageViewModel> GetPageAsync(Guid sensorId, string encodedId, long? olderThanTicks, User user)
        {
            var sensor = _cache.GetSensor(sensorId);

            if (sensor is null || olderThanTicks <= DateTime.MinValue.Ticks)
                return new FileValuesPageViewModel { EncodedId = encodedId };

            // The range ends AT the last value already shown, not a tick before it: a range ending at the first tick of a
            // week would skip that week's database (a strict overlap). That value is dropped below.
            var to = olderThanTicks is { } older
                ? new DateTime(Math.Min(older, DateTime.MaxValue.Ticks), DateTimeKind.Utc)
                : DateTime.MaxValue;

            var firstPage = olderThanTicks is null;

            // One more than a page tells whether an older page exists; one more for the last value skipped, one more for
            // the value at the end of the range.
            var (read, busy) = await ReadAsync(sensorId, DateTime.MinValue, to, PageSize + 3);

            if (busy)
                return new FileValuesPageViewModel { EncodedId = encodedId, IsBusy = true };

            var files = read.OfType<FileValue>().Where(file => olderThanTicks is null || file.Time.Ticks < olderThanTicks).ToList();

            // An ordinary last value is shown above the list with its preview; a large one has no preview and stays in it.
            if (firstPage && files.Count > 0 && sensor.LastValue is FileValue last && !IsLarge(last) && files[0].Time.Ticks == last.Time.Ticks)
                files.RemoveAt(0);

            var page = files.Take(PageSize).ToList();
            var productId = sensor.Root?.Id ?? Guid.Empty;
            var canManage = user is not null && user.IsManager(productId);
            var allUploads = firstPage ? _largeFiles.GetUploads(sensorId) : [];
            var now = Clock();
            var uploads = allUploads.Take(PageSize).Select(upload => ToUploadRow(upload, now)).ToList();
            var hasPassword = _largeFiles.HasDumpPassword(productId);
            var rows = page.Select(file => ToRow(file, sensorId, productId, hasPassword, canManage)).ToList();

            // Only a secure file waits for the password; a not secure one is given out without one.
            var listsSecure = allUploads.Any(upload => upload.IsSecure) || rows.Any(row => row.IsLarge && row.Kind == SecureKind);
            var passwordMissing = firstPage && listsSecure && !hasPassword;

            return new FileValuesPageViewModel
            {
                EncodedId = encodedId,
                Uploads = uploads,
                MoreUploads = allUploads.Count - uploads.Count,
                Rows = rows,
                OlderThanTicks = files.Count > PageSize ? page[^1].Time.Ticks : null,
                ShowPasswordMissing = passwordMissing,
                PasswordSettingsUrl = passwordMissing && canManage ? ProductSettingsPath(productId) : null,
            };
        }


        public const string SecureKind = "secure";

        public const string NotSecureKind = "not secure";

        /// <summary>
        /// Who downloaded a large file of the sensor and when, for a Manager of the product or an admin; null when the user
        /// may not see it or the file is not one of this sensor's. Read only when the user asks: the log is not read with a page.
        /// </summary>
        public LargeFileDownloadsViewModel GetDownloads(Guid sensorId, Guid fileId, User user)
        {
            var sensor = _cache.GetSensor(sensorId);
            var productId = sensor?.Root?.Id ?? Guid.Empty;

            if (sensor is null || user is null || !user.IsManager(productId) || _largeFiles.Describe(fileId)?.SensorId != sensorId)
                return null;

            IReadOnlyList<LargeFileDownloadFacts> downloads;

            try
            {
                downloads = _largeFiles.GetDownloads(productId, fileId, MaxDownloadsShown + 1);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"The download log of large file {fileId} could not be read: {ex.Message}");
                return new LargeFileDownloadsViewModel { IsUnavailable = true };
            }

            return new LargeFileDownloadsViewModel
            {
                Downloads = downloads.Take(MaxDownloadsShown).ToList(),
                IsCut = downloads.Count > MaxDownloadsShown,
            };
        }

        /// <summary>The settings page of a product, where its dump password is set.</summary>
        public static string ProductSettingsPath(Guid productId) => $"/Product/EditProduct?Product={productId}";

        /// <summary>
        /// The state of a large value's file: its record and archive first, then, for a secure file not packed, the
        /// product's password before the builder's state (a product without one waits whatever the builder last said). A not
        /// secure file never waits for a password.
        /// </summary>
        internal static (LargeFileRowState State, string Text) Decide(LargeFileStoredFacts facts, bool hasPassword)
        {
            if (facts is null)
                return (LargeFileRowState.Missing, "file is missing");

            if (facts.State == LargeFileStoredFacts.Unavailable)
                return (LargeFileRowState.Unavailable, "state cannot be read now");

            if (facts.State == "packed" && facts.DownloadUrl is not null)
                return facts.BuildState is "queued" or "building"
                    ? (LargeFileRowState.Ready, "ready (being re-encrypted under the new password)")
                    : (LargeFileRowState.Ready, "ready");

            if (facts.IsSecure && !hasPassword)
                return (LargeFileRowState.WaitingForPassword, "waiting for the product's password");

            return facts.BuildState switch
            {
                "waitingForPassword" when facts.IsSecure => (LargeFileRowState.WaitingForPassword, "waiting for the product's password"),
                "waitingForRoom" => (LargeFileRowState.WaitingForRoom, "being packed (waiting for disk space)"),
                "failed" => (LargeFileRowState.PackingFailed, "packing failed"),
                _ => (LargeFileRowState.Packing, "being packed"),
            };
        }

        /// <summary>
        /// The state of an upload that is not a value yet. Being checked only while the store runs its end check. An upload
        /// whose every piece is in without a check, or that got no piece for longer than <see cref="LargeFileUploadFacts.StalledAfter"/>
        /// (the collector went away, a check failed), waits for its collector; the store removes it at <see cref="LargeFileUploadFacts.ExpiresUtc"/>.
        /// </summary>
        internal static (LargeFileRowState State, string Text, int Percent) Decide(LargeFileUploadFacts upload, DateTime utcNow)
        {
            var percent = upload.PiecesTotal <= 0 ? 0 : (int)Math.Min(100, upload.PiecesReceived * 100 / upload.PiecesTotal);
            var allIn = upload.PiecesTotal > 0 && upload.PiecesReceived >= upload.PiecesTotal;

            if (upload.IsChecking)
                return (LargeFileRowState.Checking, "being checked", 100);

            if (allIn)
                return (LargeFileRowState.WaitingForCollector, "waiting for the collector to finish", 100);

            if (upload.StalledAfter > TimeSpan.Zero && utcNow - upload.LastPieceUtc > upload.StalledAfter)
                return (LargeFileRowState.WaitingForCollector, $"waiting for the collector, {percent}%", percent);

            return (LargeFileRowState.Uploading, $"uploading, {percent}%", percent);
        }

        /// <summary>The page's clock (the time an upload is judged stalled at). Test seam.</summary>
        internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;


        private FileValueRowViewModel ToRow(FileValue file, Guid sensorId, Guid productId, bool hasPassword, bool canManage)
        {
            var row = FileValueRowViewModel.From(file);

            if (!row.IsLarge || file.LargeFileIdOrDefault() is not { } fileId)
                return row;

            var facts = _largeFiles.Describe(fileId);

            // A value that names a file of another sensor (a disagreement the store reports) shows nothing of that file.
            if (facts is not null && facts.State != LargeFileStoredFacts.Unavailable && facts.SensorId != sensorId)
                facts = null;

            var (state, text) = Decide(facts, hasPassword);

            return new FileValueRowViewModel
            {
                TimeTicks = row.TimeTicks,
                Time = row.Time,
                Name = row.Name,
                Extension = row.Extension,
                Size = row.Size,
                Status = row.Status,
                Comment = row.Comment,
                IsLarge = true,
                Kind = facts is null || facts.State == LargeFileStoredFacts.Unavailable ? null : KindOf(facts.IsSecure),
                LargeState = state,
                StateText = text,
                Reason = state == LargeFileRowState.Ready ? null : facts?.NotReadyReason,
                DownloadUrl = state == LargeFileRowState.Ready ? facts.DownloadUrl : null,
                SourceModified = file.LargeFileSourceModifiedOrDefault(),
                Application = Shown(facts?.Application),
                Host = Shown(facts?.Host),
                FileId = fileId,
                ShowsDownloads = canManage,
            };
        }

        private static LargeFileUploadRowViewModel ToUploadRow(LargeFileUploadFacts upload, DateTime utcNow)
        {
            var (state, text, percent) = Decide(upload, utcNow);

            return new LargeFileUploadRowViewModel
            {
                FileId = upload.FileId,
                Name = upload.Name.WithoutBidiControls(),
                LastPieceUtc = upload.LastPieceUtc,
                ExpiresUtc = upload.ExpiresUtc,
                Size = upload.Size.ToReadableMemoryFormat(),
                Kind = KindOf(upload.IsSecure),
                State = state,
                StateText = text,
                Percent = percent,
                StartedUtc = upload.StartedUtc,
                SourceModified = upload.SourceModifiedUtc,
                Application = Shown(upload.Application),
                Host = Shown(upload.Host),
            };
        }

        private static string KindOf(bool secure) => secure ? SecureKind : NotSecureKind;

        private static string Shown(string value) => string.IsNullOrWhiteSpace(value) ? FileValueRowViewModel.Unknown : value.WithoutBidiControls();

        private FileValue GetLastValue(Guid sensorId) => _cache.GetSensor(sensorId)?.LastValue as FileValue;
    }
}
