using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class DoubleBarValuesStorage : BarValuesStorage<DoubleBarValue>
    {
        internal DoubleBarValuesStorage(IDatabaseCore database) : base(database) { }
    }
}
