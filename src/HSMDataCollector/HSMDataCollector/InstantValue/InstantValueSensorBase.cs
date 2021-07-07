using System;
using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMSensorDataObjects;

namespace HSMDataCollector.InstantValue
{
    [Obsolete("Use InstantValueSensor class")]
    abstract class InstantValueSensorBase : SensorBase
    {
        protected object _syncObject;
        
        protected InstantValueSensorBase(string path, string productKey, IValuesQueue queue) :
            base(path, productKey, queue)
        {
            _syncObject = new object();
        }

        public override CommonSensorValue GetLastValue()
        {
            return null;
        }

        public override void Dispose()
        {

        }

        public override bool HasLastValue => false;
    }
}
