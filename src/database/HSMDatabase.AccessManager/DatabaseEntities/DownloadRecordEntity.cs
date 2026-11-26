using System;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    // One record of the download log (ADR-0022 point 15), stored as JSON in the DownloadLog database under the key
    // "UTC ticks (8 bytes, big-endian) | record id (16 bytes)", so a time range is a key range.
    public sealed record DownloadRecordEntity
    {
        public Guid Id { get; init; }

        // UTC ticks; also the first part of the key.
        public long TimeTicks { get; init; }

        public Guid ProductId { get; init; }

        public Guid SensorId { get; init; }

        public Guid FileId { get; init; }

        public Guid ArchiveId { get; init; }

        public int PasswordVersion { get; init; }

        public string FileName { get; init; }

        // DownloadChannel value.
        public byte Channel { get; init; }

        public Guid UserId { get; init; }

        public string UserName { get; init; }

        public Guid? TokenId { get; init; }

        public string TokenName { get; init; }

        public string ClientIp { get; init; }
    }
}
