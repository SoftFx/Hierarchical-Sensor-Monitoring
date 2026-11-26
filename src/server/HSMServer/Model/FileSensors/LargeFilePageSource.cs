using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager.LargeFiles;
using HSMServer.Core.DataLayer;
using HSMServer.Core.LargeFiles.Downloads;
using HSMServer.Core.LargeFiles.DumpPasswords;
using HSMServer.Model.ManagementApi.LargeFiles;

namespace HSMServer.Model.FileSensors
{
    /// <summary>
    /// What the sensor page reads about large files: the uploads of a sensor and the origin of a file from the large file
    /// store (#1883); the state, the kind, the reason and the archive of a file in the download step's own words
    /// (<see cref="LargeFileDownloadService.DescribeFile"/>), the product's dump password and the download log (#1885).
    /// Without the archive side (a test, a tool) it answers from the store alone: nothing packed, nothing downloaded.
    /// </summary>
    public sealed class LargeFilePageSource : ILargeFilePageSource
    {
        private readonly IDatabaseCore _database;
        private readonly LargeFileDownloadService _downloads;
        private readonly IDumpPasswordService _passwords;
        private readonly IDownloadLog _log;
        private readonly NLog.Logger _logger = NLog.LogManager.GetLogger(nameof(LargeFilePageSource));


        public LargeFilePageSource(IDatabaseCore database, LargeFileDownloadService downloads, IDumpPasswordService passwords, IDownloadLog log)
        {
            _database = database;
            _downloads = downloads;
            _passwords = passwords;
            _log = log;
        }


        // A server that never took a large file has no store content: nothing is opened for a page view.
        private ILargeFileStore Store => _database.LargeFiles is { HasContent: true } store ? store : null;


        public IReadOnlyList<LargeFileUploadFacts> GetUploads(Guid sensorId)
        {
            try
            {
                return ReadUploads(sensorId);
            }
            catch (Exception ex)
            {
                // A store that cannot be read never takes the page of a File sensor down: no uploads are listed.
                _logger.Error(ex, $"The uploads of sensor {sensorId} could not be read from the large file store: {ex.Message}");
                return [];
            }
        }

        private IReadOnlyList<LargeFileUploadFacts> ReadUploads(Guid sensorId)
        {
            if (Store is not { } store)
                return [];

            var limits = store.Limits ?? new LargeFileStoreLimits();

            return store.GetFiles(LargeFileState.Uploading)
                        .Where(record => record.SensorId == sensorId)
                        .OrderByDescending(record => record.CreatedUtc)
                        .Select(record => new LargeFileUploadFacts
                        {
                            FileId = record.FileId,
                            Name = record.Name,
                            Size = record.Size,
                            SourceModifiedUtc = record.SourceModifiedUtc,
                            StartedUtc = record.CreatedUtc,
                            PiecesReceived = record.PieceCount,
                            PiecesTotal = record.TotalPieces,
                            IsChecking = store.IsChecking(record.FileId),
                            LastPieceUtc = LastPieceOf(record),
                            ExpiresUtc = LastPieceOf(record) + limits.AbandonedUploadAge,
                            StalledAfter = limits.LeaseTimeout,
                            IsSecure = record.Secure,
                            Application = record.ClientName,
                            Host = record.RemoteAddress,
                        })
                        .ToList();
        }

        public LargeFileStoredFacts Describe(Guid fileId)
        {
            try
            {
                return DescribeFile(fileId);
            }
            catch (Exception ex)
            {
                // A row whose file cannot be read now says so; it is never shown as a file that is missing.
                _logger.Error(ex, $"Large file {fileId} could not be described: {ex.Message}");
                return new LargeFileStoredFacts { FileId = fileId, State = LargeFileStoredFacts.Unavailable };
            }
        }

        private LargeFileStoredFacts DescribeFile(Guid fileId)
        {
            // The origin is the store's record alone; the download step's own store sees the same files.
            var record = Store?.TryGetFile(fileId);

            if (_downloads is not null)
            {
                // Null when the file is unknown, still uploading, or its sensor is gone or moved.
                if (_downloads.DescribeFile(fileId, baseUrl: null) is not { } description)
                    return null;

                return new LargeFileStoredFacts
                {
                    FileId = description.FileId,
                    SensorId = description.SensorId,
                    State = description.State,
                    BuildState = description.BuildState,
                    IsSecure = description.Secure,
                    NotReadyReason = description.NotReadyReason,
                    DownloadUrl = description.Archive is { } archive ? LargeFileDownloadService.WebDownloadPath(fileId, archive.Id) : null,
                    Application = record?.ClientName,
                    Host = record?.RemoteAddress,
                };
            }

            if (record is not { State: LargeFileState.Verified or LargeFileState.Packed })
                return null;

            return new LargeFileStoredFacts
            {
                FileId = record.FileId,
                SensorId = record.SensorId,
                State = record.State == LargeFileState.Packed ? "packed" : "verified",
                BuildState = "none",
                IsSecure = record.Secure,
                Application = record.ClientName,
                Host = record.RemoteAddress,
            };
        }

        public bool HasDumpPassword(Guid productId) => _passwords?.HasPassword(productId) ?? true;

        public IReadOnlyList<LargeFileDownloadFacts> GetDownloads(Guid productId, Guid fileId, int limit)
        {
            if (_log is null)
                return [];

            return _log.ReadFile(productId, fileId, limit)
                       .Select(record => new LargeFileDownloadFacts(record.TimeUtc, Who(record), record.Channel == DownloadChannel.Api ? "API" : "web"))
                       .ToList();
        }


        // A record before its first piece counts from its start.
        private static DateTime LastPieceOf(LargeFileRecord record) => record.PieceCount > 0 && record.LastPieceUtc > DateTime.MinValue ? record.LastPieceUtc : record.CreatedUtc;

        private static string Who(DownloadRecord record) =>
            record.TokenId is null ? record.UserName : $"{record.UserName} (token {record.TokenName})";
    }
}
