using HSMDataCollector.Base;

namespace HSMDataCollector.InstantValue
{
    abstract class InstantValueSensorBase : SensorBase
    {
        protected object _syncRoot;
        protected InstantValueSensorBase(string path, string productKey, string serverAddress) : base(path, productKey, serverAddress)
        {
            _syncRoot = new object();
        }
    }
}
