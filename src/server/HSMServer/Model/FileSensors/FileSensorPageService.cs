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
        /// 0. Null when there is none. The content is decompressed for an ordinary file value.
        /// </summary>
        public async Task<FileValue> GetFileAsync(Guid sensorId, long timeTicks)
        {
            if (timeTicks == default)
                return GetLastValue(sensorId)?.DecompressContent();

            if (timeTicks < DateTime.MinValue.Ticks || timeTicks > DateTime.MaxValue.Ticks)
                return null;

            var time = new DateTime(timeTicks, DateTimeKind.Utc);
            var values = await _cache.GetSensorValuesPage(sensorId, time, time, 1).Flatten();

            return values.OfType<FileValue>().FirstOrDefault(file => file.Time.Ticks == timeTicks)?.DecompressContent();
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

            var to = olderThanTicks is { } older
                ? new DateTime(Math.Min(older - 1, DateTime.MaxValue.Ticks), DateTimeKind.Utc)
                : DateTime.MaxValue;

            var firstPage = olderThanTicks is null;

            // One more than a page tells whether an older page exists; one more again for the last value skipped.
            var read = await _cache.GetSensorValuesPage(sensorId, DateTime.MinValue, to, PageSize + 2).Flatten();
            var files = read.OfType<FileValue>().ToList();

            // An ordinary last value is shown above the list with its preview; a large one has no preview and stays in it.
            if (firstPage && files.Count > 0 && sensor.LastValue is FileValue last && !IsLarge(last) && files[0].Time.Ticks == last.Time.Ticks)
                files.RemoveAt(0);

            var page = files.Take(PageSize).ToList();
            var productId = sensor.Root?.Id ?? Guid.Empty;
            var canManage = user is not null && user.IsManager(productId);
            var allUploads = firstPage ? _largeFiles.GetUploads(sensorId) : [];
            var uploads = allUploads.Take(PageSize).Select(ToUploadRow).ToList();
            var hasPassword = _largeFiles.HasDumpPassword(productId);
            var rows = page.Select(file => ToRow(file, productId, hasPassword, canManage)).ToList();

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

        internal static (LargeFileRowState State, string Text, int Percent) Decide(LargeFileUploadFacts upload)
        {
            var percent = upload.PiecesTotal <= 0 ? 0 : (int)Math.Min(100, upload.PiecesReceived * 100 / upload.PiecesTotal);
            var allIn = upload.PiecesTotal > 0 && upload.PiecesReceived >= upload.PiecesTotal;

            return upload.IsChecking || allIn
                ? (LargeFileRowState.Checking, "being checked", 100)
                : (LargeFileRowState.Uploading, $"uploading, {percent}%", percent);
        }


        private FileValueRowViewModel ToRow(FileValue file, Guid productId, bool hasPassword, bool canManage)
        {
            var row = FileValueRowViewModel.From(file);

            if (!row.IsLarge || file.LargeFileIdOrDefault() is not { } fileId)
                return row;

            var facts = _largeFiles.Describe(fileId);
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
                Kind = facts is null ? null : KindOf(facts.IsSecure),
                LargeState = state,
                StateText = text,
                Reason = state == LargeFileRowState.Ready ? null : facts?.NotReadyReason,
                DownloadUrl = state == LargeFileRowState.Ready ? facts.DownloadUrl : null,
                SourceModified = file.LargeFileSourceModifiedOrDefault(),
                Application = Shown(facts?.Application),
                Host = Shown(facts?.Host),
                Downloads = canManage ? _largeFiles.GetDownloads(productId, fileId, MaxDownloadsShown) : null,
            };
        }

        private static LargeFileUploadRowViewModel ToUploadRow(LargeFileUploadFacts upload)
        {
            var (state, text, percent) = Decide(upload);

            return new LargeFileUploadRowViewModel
            {
                FileId = upload.FileId,
                Name = upload.Name,
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

        private static string Shown(string value) => string.IsNullOrWhiteSpace(value) ? FileValueRowViewModel.Unknown : value;

        private FileValue GetLastValue(Guid sensorId) => _cache.GetSensor(sensorId)?.LastValue as FileValue;
    }
}
