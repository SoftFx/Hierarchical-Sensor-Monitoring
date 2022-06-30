using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class FileValuesStorage : ValuesStorage<FileValue>
    {
        internal FileValuesStorage(IDatabaseCore database) : base(database) { }
    }
}
