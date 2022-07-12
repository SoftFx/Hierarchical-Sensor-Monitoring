using HSMServer.Core.Helpers;

namespace HSMServer.Core.Model
{
    public sealed class FileValuesStorage : ValuesStorage<FileValue>
    {
        protected override int CacheSize => 1;

        internal override FileValue AddValue(FileValue value)
        {
            var compressed = value.CompressContent();

            base.AddValue(compressed);

            return compressed;
        } 
    }
}
