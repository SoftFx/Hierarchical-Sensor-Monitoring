using HSMDataCollector.Core;

namespace HSMDataCollector.InstantValue
{
    abstract class InstantValueTypedSensorBase<T> : InstantValueSensorBase
    {
        protected InstantValueTypedSensorBase(string path, string productKey, IValuesQueue queue)
            : base(path, productKey, queue)
        {
        }
    }
}
