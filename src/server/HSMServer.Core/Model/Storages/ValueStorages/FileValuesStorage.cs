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
            if (value.OriginalSize != value.Value.Length)
                value = value.DecompressContent();

            _lastValue = value;

            return base.AddValueBase(value);
        }

        internal override FileValue AddValue(FileValue value) =>
            base.AddValue(value.CompressContent());
    }
}
