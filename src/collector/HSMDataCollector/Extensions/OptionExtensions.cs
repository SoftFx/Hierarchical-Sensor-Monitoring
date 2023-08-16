using HSMDataCollector.Options;
using HSMSensorDataObjects;
using System;

namespace HSMDataCollector.Extensions
{
    internal static class OptionExtensions
    {
        internal static InstantSensorOptions SetInstantType<T>(this InstantSensorOptions options)
        {
            switch (typeof(T))
            {
                case Type type when type == typeof(bool):
                    return (InstantSensorOptions)options.SetType(SensorType.BooleanSensor);
                case Type type when type == typeof(int):
                    return (InstantSensorOptions)options.SetType(SensorType.IntSensor);
                case Type type when type == typeof(double):
                    return (InstantSensorOptions)options.SetType(SensorType.DoubleSensor);
                case Type type when type == typeof(string):
                    return (InstantSensorOptions)options.SetType(SensorType.StringSensor);
                case Type type when type == typeof(TimeSpan):
                    return (InstantSensorOptions)options.SetType(SensorType.TimeSpanSensor);
                case Type type when type == typeof(Version):
                    return (InstantSensorOptions)options.SetType(SensorType.VersionSensor);
                default:
                    throw new ArgumentException($"Unsupported sensor value {typeof(T).Name}");
            }
        }


        internal static SensorOptions2 SetBarType<T>(this SensorOptions2 options)
        {
            switch (typeof(T))
            {
                case Type type when type == typeof(int):
                    return options.SetType(SensorType.IntegerBarSensor);
                case Type type when type == typeof(double):
                    return options.SetType(SensorType.DoubleBarSensor);
                default:
                    throw new ArgumentException($"Unsupported sensor value {typeof(T).Name}");
            }
        }
    }
}