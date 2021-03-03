using HSMDataCollector.Base;
using HSMDataCollector.Core;

namespace HSMDataCollector.InstantValue
{
    abstract class InstantValueSensorBase : SensorBase
    {
        protected object _syncObject;
        protected InstantValueSensorBase(string path, string productKey, string serverAddress, IValuesQueue queue) :
            base(path, productKey, serverAddress, queue)
        {
            _syncObject = new object();
        }
    }
}
