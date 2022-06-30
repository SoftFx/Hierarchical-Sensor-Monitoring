using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public abstract class BarValuesStorage<T> : ValuesStorage<T> where T : BarBaseValue, new()
    {
        internal BarValuesStorage(IDatabaseCore database) : base(database) { }
    }
}
