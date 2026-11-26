using System;

namespace HSMServer.Core.Model.HistoryValues
{
    /// <summary>
    /// The description of a large file value in the history an access key reads (#1885): what the file is and where it
    /// stands, never its content. An access key never downloads: the archive is reached through the management API or
    /// the web UI.
    /// </summary>
    public sealed class LargeFileHistory
    {
        /// <summary>The id of the file in the large file store.</summary>
        public Guid FileId { get; init; }

        /// <summary>The original file name as the collector sent it.</summary>
        public string Name { get; init; }

        public string Extension { get; init; }

        /// <summary>The original size in bytes.</summary>
        public long Size { get; init; }

        /// <summary>The kind of the file, from the store's record; null when the store does not hold the file now or could not be read.</summary>
        public bool? Secure { get; init; }

        /// <summary>"verified", "packed", "removed" (the store no longer holds the file) or "unavailable" (the store cannot be read now).</summary>
        public string State { get; init; }
    }
}
