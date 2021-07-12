using System;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.Core
{
    public interface IValuesQueue
    {
        [Obsolete]
        void Enqueue(CommonSensorValue value);
        void EnqueueData(UnitedSensorValue value);
    }
}