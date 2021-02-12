using System.Net.Http;

namespace HSMDataCollector.InstantValue
{
    abstract class InstantValueTypedSensorBase<T> : InstantValueSensorBase where T : struct
    {
        protected T Value;
        
        protected InstantValueTypedSensorBase(string path, string productKey, string address, HttpClient client)
            : base(path, productKey, address, client)
        {
        }
    }
}
