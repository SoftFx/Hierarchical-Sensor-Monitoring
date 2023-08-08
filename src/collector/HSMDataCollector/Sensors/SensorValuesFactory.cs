using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.SensorsFactory
{
    internal static class SensorValuesFactory
    {
        internal static SensorValueBase BuildValue<T>(T val)
        {
            if (val is SensorValueBase sensorV)
                return sensorV;

            switch (typeof(T))
            {
                case Type type when type == typeof(bool):
                    return new BoolSensorValue()
                    {
                        Value = val is bool boolV && boolV
                    };

                case Type type when type == typeof(int):
                    return new IntSensorValue()
                    {
                        Value = val is int intV ? intV : default
                    };

                case Type type when type == typeof(double):
                    return new DoubleSensorValue()
                    {
                        Value = val is double doubleV ? doubleV : default
                    };

                case Type type when type == typeof(string):
                    return new StringSensorValue()
                    {
                        Value = val is string stringV ? stringV : default
                    };

                case Type type when type == typeof(TimeSpan):
                    return new TimeSpanSensorValue()
                    {
                        Value = val is TimeSpan time ? time : default
                    };

                case Type type when type == typeof(Version):
                    return new VersionSensorValue()
                    {
                        Value = val is Version version ? version : default
                    };
                default:
                    throw new ArgumentException($"Unsupported sensor value {typeof(T).Name}");
            }
        }
    }
}
