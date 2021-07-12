using System;
using HSMSensorDataObjects;

namespace HSMDataCollector.PublicInterface
{
    [Obsolete("07.07.2021. Use ILastValueSensor.")]
    public interface IDefaultValueSensorInt
    {
        void AddValue(int value);
        void AddValue(int value, string comment);
        void AddValue(int value, SensorStatus status, string comment = null);
    }
}