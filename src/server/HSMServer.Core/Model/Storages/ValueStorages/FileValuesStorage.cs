using HSMServer.Core.Helpers;

namespace HSMServer.Core.Model
{
    public sealed class FileValuesStorage : ValuesStorage<FileValue>
    {
        private FileValue _lastValue;


        protected override int CacheSize => 1;

        internal override BaseValue LastValue => _lastValue;


        internal override FileValue AddValueBase(FileValue value)
        {
            _lastValue = value.OriginalSize != value.Value.Length
                ? value.DecompressContent()
                : value;

            return base.AddValueBase(value);
        }

        internal override FileValue AddValue(FileValue value) =>
            base.AddValue(value.CompressContent());
    }
}
