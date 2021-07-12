using System;
using HSMSensorDataObjects;

namespace HSMDataCollector.PublicInterface
{
    [Obsolete("Use IInstantValueSensor<T>")]
    public interface IStringSensor
    {
        void AddValue(string value);
        void AddValue(string value, string comment);
        void AddValue(string value, SensorStatus status, string comment = null);
    }
}