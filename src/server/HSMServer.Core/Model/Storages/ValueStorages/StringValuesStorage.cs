using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class StringValuesStorage : ValuesStorage<StringValue>
    {
        internal StringValuesStorage(IDatabaseCore database) : base(database) { }
    }
}
