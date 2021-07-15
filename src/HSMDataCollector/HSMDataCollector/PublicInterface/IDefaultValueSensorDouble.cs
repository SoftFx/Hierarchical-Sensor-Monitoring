using System;
using HSMSensorDataObjects;

namespace HSMDataCollector.PublicInterface
{
    [Obsolete("07.07.2021. Use ILastValueSensor.")]
    public interface IDefaultValueSensorDouble
    {
        void AddValue(double value);
        void AddValue(double value, string comment);
        void AddValue(double value, SensorStatus status, string comment = null);
    }
}