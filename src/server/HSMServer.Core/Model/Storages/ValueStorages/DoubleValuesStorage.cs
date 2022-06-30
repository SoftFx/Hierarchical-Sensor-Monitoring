using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class DoubleValuesStorage : ValuesStorage<DoubleValue>
    {
        internal DoubleValuesStorage(IDatabaseCore database) : base(database) { }
    }
}
