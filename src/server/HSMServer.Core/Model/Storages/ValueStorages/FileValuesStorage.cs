using HSMServer.Core.Extensions;

namespace HSMServer.Core.Model
{
    public sealed class FileValuesStorage : ValuesStorage<FileValue>
    {
        protected override int CacheSize => 1;


        internal override void AddValueBase(FileValue value) =>
            base.AddValueBase(value.DecompressContent());

        internal override void AddValue(FileValue value) =>
            base.AddValue(value.CompressContent());
    }
}
