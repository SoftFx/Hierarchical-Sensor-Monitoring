using System;
using System.Collections.Generic;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;

namespace HSMServer.Core.Tests
{
    internal static class SensorValuesFactory
    {
        private static DatabaseAdapterFixture _databaseFixture;


        internal static void Initialize(DatabaseAdapterFixture dbFixture) =>
            _databaseFixture = dbFixture;


        internal static BoolSensorValue NewBoolSensorValue()
        {
            var boolSensorValue = new BoolSensorValue()
            {
                BoolValue = RandomValues.GetRandomBool(),
            };

            return boolSensorValue.FillCommonSensorValueProperties();
        }

        internal static IntSensorValue NewIntSensorValue()
        {
            var intSensorValue = new IntSensorValue()
            {
                IntValue = RandomValues.GetRandomInt(),
            };

            return intSensorValue.FillCommonSensorValueProperties();
        }

        internal static DoubleSensorValue NewDoubleSensorValue()
        {
            var doubleSensorValue = new DoubleSensorValue()
            {
                DoubleValue = RandomValues.GetRandomDouble(),
            };

            return doubleSensorValue.FillCommonSensorValueProperties();
        }

        internal static StringSensorValue NewStringSensorValue()
        {
            var stringSensorValue = new StringSensorValue()
            {
                StringValue = RandomValues.GetRandomString(),
            };

            return stringSensorValue.FillCommonSensorValueProperties();
        }

        internal static IntBarSensorValue NewIntBarSensorValue()
        {
            var intBarSensorValue = new IntBarSensorValue()
            {
                LastValue = RandomValues.GetRandomInt(),
                Min = RandomValues.GetRandomInt(),
                Max = RandomValues.GetRandomInt(),
                Mean = RandomValues.GetRandomInt(),
                Percentiles = GetPercentileValuesInt(),
            };

            return intBarSensorValue.FillCommonBarSensorValueProperties();
        }

        internal static DoubleBarSensorValue NewDoubleBarSensorValue()
        {
            var doubleBarSensorValue = new DoubleBarSensorValue()
            {
                LastValue = RandomValues.GetRandomDouble(),
                Min = RandomValues.GetRandomDouble(),
                Max = RandomValues.GetRandomDouble(),
                Mean = RandomValues.GetRandomDouble(),
                Percentiles = GetPercentileValuesDouble(),
            };

            return doubleBarSensorValue.FillCommonBarSensorValueProperties();
        }

        internal static FileSensorBytesValue NewFileSensorBytesValue()
        {
            var fileSensorBytesValue = new FileSensorBytesValue()
            {
                Extension = RandomValues.GetRandomString(3),
                FileContent = RandomValues.GetRandomBytes(),
                FileName = nameof(FileSensorBytesValue),
            };

            return fileSensorBytesValue.FillCommonSensorValueProperties();
        }

        internal static FileSensorValue NewFileSensorValue()
        {
            var fileSensorValue = new FileSensorValue()
            {
                Extension = RandomValues.GetRandomString(3),
                FileContent = RandomValues.GetRandomString(),
                FileName = nameof(FileSensorValue),
            };

            return fileSensorValue.FillCommonSensorValueProperties();
        }

        private static T FillCommonBarSensorValueProperties<T>(this T sensorValue)
        {
            if (sensorValue is BarSensorValueBase barSensorValue)
            {
                barSensorValue.StartTime = DateTime.UtcNow.AddSeconds(-10);
                barSensorValue.EndTime = DateTime.UtcNow.AddSeconds(10);
                barSensorValue.Count = RandomValues.GetRandomInt(positive: true);
            }

            return sensorValue.FillCommonSensorValueProperties();
        }

        private static T FillCommonSensorValueProperties<T>(this T sensorValue)
        {
            if (sensorValue is SensorValueBase sensorValueBase)
            {
                sensorValueBase.Key = _databaseFixture.TestProduct.Key;
                sensorValueBase.Path = $"{typeof(T)}";
                sensorValueBase.Description = $"{typeof(T)} {nameof(SensorValueBase.Description)}";
                sensorValueBase.Comment = $"{typeof(T)} {nameof(SensorValueBase.Comment)}";
                sensorValueBase.Time = DateTime.UtcNow;
            }

            return sensorValue;
        }

        private static List<PercentileValueInt> GetPercentileValuesInt(int capacity = 2)
        {
            var percentiles = new List<PercentileValueInt>(capacity);

            for (int i = 0; i < capacity; ++i)
                percentiles.Add(new PercentileValueInt(RandomValues.GetRandomInt(), RandomValues.GetRandomDouble()));

            return percentiles;
        }

        private static List<PercentileValueDouble> GetPercentileValuesDouble(int capacity = 2)
        {
            var percentiles = new List<PercentileValueDouble>(capacity);

            for (int i = 0; i < capacity; ++i)
                percentiles.Add(new PercentileValueDouble(RandomValues.GetRandomDouble(), RandomValues.GetRandomDouble()));

            return percentiles;
        }
    }


    internal static class RandomValues
    {
        private static readonly Random _random = new();


        internal static bool GetRandomBool() => _random.Next(0, 2) > 0;

        internal static int GetRandomInt(bool positive = false) =>
            _random.Next(positive ? 0 : -100, 100);

        internal static double GetRandomDouble() =>
            _random.NextDouble() * (GetRandomBool() ? -100 : 100);

        internal static string GetRandomString(int capacity = 8)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

            var stringChars = new char[capacity];

            for (int i = 0; i < capacity; i++)
                stringChars[i] = chars[_random.Next(chars.Length)];

            return new string(stringChars);
        }

        internal static byte[] GetRandomBytes(int capacity = 8)
        {
            var bytes = new byte[capacity];

            for (int i = 0; i < capacity; i++)
                bytes[i] = (byte)_random.Next(0, 255);

            return bytes;
        }
    }
}
