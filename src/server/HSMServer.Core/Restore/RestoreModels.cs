using System;
using System.Collections.Generic;

namespace HSMServer.Core.Restore
{
    public enum CollisionResolution
    {
        Overwrite = 0,
        Skip = 1,
        Duplicate = 2,
    }

    public sealed record BackupFileInfo
    {
        public string FileName { get; init; }

        public DateTime LastWriteTimeUtc { get; init; }

        public long SizeBytes { get; init; }
    }

    public sealed record BackupTemplateItem
    {
        public Guid Id { get; init; }

        public string Name { get; init; }
    }

    public sealed record RestoreRequestItem
    {
        public Guid Id { get; init; }

        public CollisionResolution Resolution { get; init; }
    }

    public sealed record RestoreResultItem
    {
        public Guid Id { get; init; }

        public string Name { get; init; }

        public CollisionResolution Resolution { get; init; }

        // "inserted" / "overwritten" / "skipped" / "duplicated as <newGuid>" / "error: …"
        public string Outcome { get; init; }
    }

    public sealed record RestoreResult
    {
        public List<RestoreResultItem> Items { get; init; } = [];
    }
}
