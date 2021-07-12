using System;
using HSMSensorDataObjects;

namespace HSMDataCollector.PublicInterface
{
    [Obsolete("Use IInstantValueSensor<T>")]
    public interface IDoubleSensor
    {
        void AddValue(double value);
        void AddValue(double value, string comment);
        void AddValue(double value, SensorStatus status, string comment = null);
    }
}