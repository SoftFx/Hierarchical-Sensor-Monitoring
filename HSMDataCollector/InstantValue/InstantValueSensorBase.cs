using System.Net.Http;
using HSMDataCollector.Base;

namespace HSMDataCollector.InstantValue
{
    abstract class InstantValueSensorBase : SensorBase
    {
        protected object _syncObject;
        protected InstantValueSensorBase(string path, string productKey, string serverAddress, HttpClient client) :
            base(path, productKey, serverAddress, client)
        {
            _syncObject = new object();
        }
    }
}
