using System;

namespace HSMServer.Model.ManagementApi.LargeFiles
{
    /// <summary>
    /// A large file value as the management API and the MCP tool describe it: what it is, whether it can be downloaded
    /// and, once packed, its archive and the link to it. The dump password is never part of it: the operator gives it.
    /// </summary>
    public sealed record LargeFileDto
    {
        public Guid FileId { get; init; }

        public Guid SensorId { get; init; }

        /// <summary>The original file name as the collector sent it.</summary>
        public string Name { get; init; }

        public string Extension { get; init; }

        /// <summary>The original (decompressed) size in bytes.</summary>
        public long Size { get; init; }

        public DateTime SourceModifiedUtc { get; init; }

        /// <summary>
        /// The kind of the file, decided by the application at the send and never changed. True: the archive is a zip
        /// encrypted with AES-256 under the product's dump password. False: the same zip without encryption, opened without
        /// a password; it never waits for a password and has no password version.
        /// </summary>
        public bool Secure { get; init; }

        /// <summary>"verified" (kept, no archive yet) or "packed" (an archive can be downloaded).</summary>
        public string State { get; init; }

        /// <summary>
        /// What the archive builder is doing with the file: "none", "waitingForPassword", "queued", "building",
        /// "waitingForRoom" or "failed". A packed file being re-encrypted under a new password stays downloadable.
        /// </summary>
        public string BuildState { get; init; }

        /// <summary>Why the file cannot be downloaded yet; null when it can.</summary>
        public string NotReadyReason { get; init; }

        /// <summary>The current archive; null until the file is packed.</summary>
        public LargeFileArchiveDto Archive { get; init; }
    }


    /// <summary>
    /// The current archive of a packed large file: a zip with one entry, AES-256 under the product's dump password for a
    /// secure file, without encryption for a file that is not secure.
    /// </summary>
    public sealed record LargeFileArchiveDto
    {
        /// <summary>The archive id: a new password makes a new archive with a new id, and the old link answers 404.</summary>
        public Guid Id { get; init; }

        /// <summary>The archive size in bytes.</summary>
        public long Size { get; init; }

        /// <summary>SHA-256 of the archive bytes, lowercase hex (the download carries it as Repr-Digest, base64).</summary>
        public string Sha256 { get; init; }

        /// <summary>The version of the product's dump password the archive opens with; null for a file that is not secure.</summary>
        public int? PasswordVersion { get; init; }

        /// <summary>When that password version was set; null when it is no longer kept, and for a file that is not secure.</summary>
        public DateTime? PasswordSetUtc { get; init; }

        /// <summary>GET with the same bearer token downloads the archive (byte ranges supported).</summary>
        public string DownloadUrl { get; init; }
    }
}
