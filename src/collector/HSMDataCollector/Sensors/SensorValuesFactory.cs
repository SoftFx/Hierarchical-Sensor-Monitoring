using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.SensorsFactory
{
    internal static class SensorValuesFactory
    {
        internal static SensorType GetInstantType<T>()
        {
            switch (typeof(T))
            {
                case Type type when type == typeof(bool):
                    return SensorType.BooleanSensor;
                case Type type when type == typeof(int):
                    return SensorType.IntSensor;
                case Type type when type == typeof(double):
                    return SensorType.DoubleSensor;
                case Type type when type == typeof(string):
                    return SensorType.StringSensor;
                case Type type when type == typeof(TimeSpan):
                    return SensorType.TimeSpanSensor;
                case Type type when type == typeof(Version):
                    return SensorType.VersionSensor;
                default:
                    throw new ArgumentException($"Unsupported instant sensor type {typeof(T).Name}");
            }
        }


        internal static SensorType GetBarType<T>()
        {
            switch (typeof(T))
            {
                case Type type when type == typeof(int):
                    return SensorType.IntegerBarSensor;
                case Type type when type == typeof(double):
                    return SensorType.DoubleBarSensor;
                default:
                    throw new ArgumentException($"Unsupported bar sensor type {typeof(T).Name}");
            }
        }


        internal static Func<T, SensorValueBase> GetValueBuilder<T>(SensorType type)
        {
            switch (type)
            {
                case SensorType.BooleanSensor:
                    return (val) => new BoolSensorValue()
                    {
                        Value = val is bool boolV && boolV
                    };
                case SensorType.IntSensor:
                    return (val) => new IntSensorValue()
                    {
                        Value = val is int intV ? intV : default
                    };
                case SensorType.DoubleSensor:
                    return (val) => new DoubleSensorValue()
                    {
                        Value = val is double doubleV ? doubleV : default
                    };
                case SensorType.StringSensor:
                    return (val) => new StringSensorValue()
                    {
                        Value = val is string stringV ? stringV : default
                    };
                case SensorType.TimeSpanSensor:
                    return (val) => new TimeSpanSensorValue()
                    {
                        Value = val is TimeSpan time ? time : default
                    };
                case SensorType.VersionSensor:
                    return (val) => new VersionSensorValue()
                    {
                        Value = val is Version version ? version : default
                    };
                case SensorType.RateSensor:
                    return (val) => new RateSensorValue()
                    {
                        Value = val is double doubleV ? doubleV : default
                    };
                case SensorType.FileSensor:
                    return (val) => new FileSensorValue()
                    {
                        Value = val is List<byte> bytes ? bytes : default
                    };
                default:
                    return null;
            }
        }

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