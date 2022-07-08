namespace HSMServer.Core.Model
{
    public sealed class FileValuesStorage : ValuesStorage<FileValue>
    {
        protected override int CacheSize => 1;
    }
}
