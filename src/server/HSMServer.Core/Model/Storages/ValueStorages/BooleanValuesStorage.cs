using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class BooleanValuesStorage : ValuesStorage<BooleanValue>
    {
        internal BooleanValuesStorage(IDatabaseCore database) : base(database) { }
    }
}
