using System.Net.Http;
using HSMDataCollector.Core;

namespace HSMDataCollector.InstantValue
{
    abstract class InstantValueTypedSensorBase<T> : InstantValueSensorBase where T : struct
    {
        protected T Value;
        
        protected InstantValueTypedSensorBase(string path, string productKey, string address, IValuesQueue queue)
            : base(path, productKey, address, queue)
        {
        }
    }
}
