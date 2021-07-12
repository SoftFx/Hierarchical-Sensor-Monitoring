using System;
using HSMDataCollector.Core;

namespace HSMDataCollector.InstantValue
{
    [Obsolete("Use InstantValueSensor class")]
    abstract class InstantValueTypedSensorBase<T> : InstantValueSensorBase
    {
        protected InstantValueTypedSensorBase(string path, string productKey, IValuesQueue queue)
            : base(path, productKey, queue)
        {
        }
    }
}
