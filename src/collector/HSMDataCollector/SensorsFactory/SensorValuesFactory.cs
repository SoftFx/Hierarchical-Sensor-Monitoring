using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.SensorsFactory
{
    internal static class SensorValuesFactory
    {
        internal static SensorValueBase BuildValue<T>(T val)
        {
            switch (val)
            {
                case bool boolV:
                    return new BoolSensorValue() { Value = boolV };
                case int intV:
                    return new IntSensorValue() { Value = intV };
                case double doubleV:
                    return new DoubleSensorValue() { Value = doubleV };
                case string stringV:
                    return new StringSensorValue() { Value = stringV };
                case TimeSpan timeSpanV:
                    return new TimeSpanSensorValue() { Value = timeSpanV };
                case SensorValueBase sensorV:
                    return sensorV;
                default:
                    return null;
            }
        }
    }
}
