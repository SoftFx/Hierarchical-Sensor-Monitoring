using HSMDataCollector.Base;

namespace HSMDataCollector.InstantValue
{
    abstract class InstantValueSensorBase : SensorBase
    {
        protected object _syncRoot;
        protected InstantValueSensorBase(string name, string path, string productKey, string serverAddress) : base(name, path, productKey, serverAddress)
        {
            _syncRoot = new object();
        }
    }
}
