using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class IntegerBarValuesStorage : BarValuesStorage<IntegerBarValue>
    {
        internal IntegerBarValuesStorage(IDatabaseCore database) : base(database) { }
    }
}
