using HSMCommon.Model;
using HSMServer.Core.Extensions;
using System;


namespace HSMServer.Core.Model
{
    public sealed class FileValuesStorage : ValuesStorage<FileValue>
    {
        protected override int CacheSize => 1;


        internal override void AddValueBase(FileValue value) => base.AddValueBase(value.DecompressContent());

        internal override void AddValue(FileValue value) => base.AddValue(value.CompressContent());

        // The #1441 out-of-order direct write bypasses AddValue, which is
        // where incoming file content is compressed for storage — apply the
        // same write-side transform to the copy being persisted, so a late
        // file value occupies the same space it would through the normal
        // path. DecompressContent on the read path passes through when
        // Value.Length == OriginalSize, so an incompressible payload stays
        // raw either way.
        internal override FileValue PrepareForPersist(FileValue value) => value.CompressContent();
    }
}