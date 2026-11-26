using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager.LargeFiles;
using HSMServer.Core.DataLayer;

namespace HSMServer.Model.FileSensors
{
    /// <summary>
    /// What the sensor page reads from the large file store (#1883): the uploads of a sensor and the record of a file. The
    /// archive, the build, the dump password and the download log are the download step's (#1885).
    /// </summary>
    public sealed class StoreLargeFilePageSource : ILargeFilePageSource
    {
        private readonly IDatabaseCore _database;


        public StoreLargeFilePageSource(IDatabaseCore database)
        {
            _database = database;
        }


        // A server that never took a large file has no store content: nothing is opened for a page view.
        private ILargeFileStore Store => _database.LargeFiles is { HasContent: true } store ? store : null;


        public IReadOnlyList<LargeFileUploadFacts> GetUploads(Guid sensorId)
        {
            if (Store is not { } store)
                return [];

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
                            IsSecure = record.Secure,
                            Application = record.ClientName,
                            Host = record.RemoteAddress,
                        })
                        .ToList();
        }

        public LargeFileStoredFacts Describe(Guid fileId)
        {
            if (Store?.TryGetFile(fileId) is not { State: LargeFileState.Verified or LargeFileState.Packed } record)
                return null;

            return new LargeFileStoredFacts
            {
                FileId = record.FileId,
                State = record.State == LargeFileState.Packed ? "packed" : "verified",
                BuildState = "none",
                IsSecure = record.Secure,
                Application = record.ClientName,
                Host = record.RemoteAddress,
            };
        }

        public bool HasDumpPassword(Guid productId) => true;

        public IReadOnlyList<LargeFileDownloadFacts> GetDownloads(Guid productId, Guid fileId, int limit) => [];
    }
}
