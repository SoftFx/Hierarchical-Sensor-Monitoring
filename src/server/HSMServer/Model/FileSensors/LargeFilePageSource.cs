using System;
using System.Collections.Generic;

namespace HSMServer.Model.FileSensors
{
    /// <summary>An upload of a File sensor that is not a value yet: its part in the large file store.</summary>
    public sealed record LargeFileUploadFacts
    {
        public Guid FileId { get; init; }

        /// <summary>The original file name, as the collector named it.</summary>
        public string Name { get; init; }

        public long Size { get; init; }

        public DateTime SourceModifiedUtc { get; init; }

        public DateTime StartedUtc { get; init; }

        public long PiecesReceived { get; init; }

        public long PiecesTotal { get; init; }

        /// <summary>The end check of the upload is running (or every piece is in and the file is not verified yet).</summary>
        public bool IsChecking { get; init; }

        /// <summary>The collector's client name; null when the record has none.</summary>
        public string Application { get; init; }

        /// <summary>The address the collector connected from; null when the record has none.</summary>
        public string Host { get; init; }
    }


    /// <summary>
    /// A verified or packed large file, in the vocabulary of the download (#1885): <see cref="State"/> "verified" or
    /// "packed", <see cref="BuildState"/> "none", "queued", "building", "waitingForPassword", "waitingForRoom" or "failed".
    /// </summary>
    public sealed record LargeFileStoredFacts
    {
        public Guid FileId { get; init; }

        public string State { get; init; }

        public string BuildState { get; init; }

        /// <summary>Why the file cannot be downloaded yet; null when it can.</summary>
        public string NotReadyReason { get; init; }

        /// <summary>The web download link of the current archive; null until the file is packed.</summary>
        public string DownloadUrl { get; init; }

        public string Application { get; init; }

        public string Host { get; init; }
    }


    /// <summary>One allowed download of a file, from the download log.</summary>
    public sealed record LargeFileDownloadFacts(DateTime TimeUtc, string Who, string Channel);


    /// <summary>
    /// What the sensor page reads about large files (#1886) from the large file store (#1883), the archive builder, the
    /// dump passwords and the download log (#1885). The caller has checked that the user may see the product.
    /// </summary>
    public interface ILargeFilePageSource
    {
        /// <summary>The uploads of the sensor that are not values yet, newest first.</summary>
        IReadOnlyList<LargeFileUploadFacts> GetUploads(Guid sensorId);

        /// <summary>The state of the file of a large value; null when the store has no record of it.</summary>
        LargeFileStoredFacts Describe(Guid fileId);

        bool HasDumpPassword(Guid productId);

        /// <summary>The downloads of one file, newest first.</summary>
        IReadOnlyList<LargeFileDownloadFacts> GetDownloads(Guid productId, Guid fileId, int limit);
    }


    /// <summary>A server without the large file store: no uploads, no records.</summary>
    public sealed class NoLargeFilePageSource : ILargeFilePageSource
    {
        public IReadOnlyList<LargeFileUploadFacts> GetUploads(Guid sensorId) => [];

        public LargeFileStoredFacts Describe(Guid fileId) => null;

        public bool HasDumpPassword(Guid productId) => true;

        public IReadOnlyList<LargeFileDownloadFacts> GetDownloads(Guid productId, Guid fileId, int limit) => [];
    }
}
