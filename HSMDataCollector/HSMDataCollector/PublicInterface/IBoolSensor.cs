using System;
using HSMSensorDataObjects;

namespace HSMDataCollector.PublicInterface
{
    public interface IBoolSensor
    {
        void AddValue(bool value);
        void AddValue(bool value, string comment);
        void AddValue(bool value, SensorStatus status, string comment = null);
    }
}