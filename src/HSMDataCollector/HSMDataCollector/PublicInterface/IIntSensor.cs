using System;
using HSMSensorDataObjects;

namespace HSMDataCollector.PublicInterface
{
    [Obsolete("Use IInstantValueSensor<T>")]
    public interface IIntSensor
    {
        void AddValue(int value);
        void AddValue(int value, string comment);
        void AddValue(int value, SensorStatus status, string comment = null);
    }
}