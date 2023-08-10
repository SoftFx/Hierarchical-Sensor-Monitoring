using HSMDataCollector.Options;
using HSMSensorDataObjects;
using System;

namespace HSMDataCollector.Extensions
{
    internal static class OptionExtensions
    {
        internal static SensorOptions2 SetInstantType<T>(this SensorOptions2 options)
        {
            switch (typeof(T))
            {
                case Type type when type == typeof(bool):
                    return options.SetType(SensorType.BooleanSensor);
                case Type type when type == typeof(int):
                    return options.SetType(SensorType.IntSensor);
                case Type type when type == typeof(double):
                    return options.SetType(SensorType.DoubleSensor);
                case Type type when type == typeof(string):
                    return options.SetType(SensorType.StringSensor);
                case Type type when type == typeof(TimeSpan):
                    return options.SetType(SensorType.TimeSpanSensor);
                case Type type when type == typeof(Version):
                    return options.SetType(SensorType.VersionSensor);
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