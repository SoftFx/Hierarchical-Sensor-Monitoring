using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class IntegerValuesStorage : ValuesStorage<IntegerValue>
    {
        internal IntegerValuesStorage(IDatabaseCore database) : base(database) { }
    }
}
