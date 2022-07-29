using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;
using SensorType = HSMSensorDataObjects.SensorType;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class SensorValuesTester
    {
        internal static void TestSensorHistoryDataFromExtendedBarSensorData(ExtendedBarSensorData expected, SensorHistoryData actual)
        {
            Assert.Equal(expected.ValueType, actual.SensorType);

            TestSensorHistoryDataFromDB(expected.Value, actual);
        }

        internal static void TestSensorHistoryDataFromDB(SensorValueBase expected, SensorHistoryData actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(GetSensorValueType(expected), actual.SensorType);
            Assert.Equal(expected.Time.ToUniversalTime(), actual.Time);
            Assert.Contains(expected.Comment, actual.TypedData);
            Assert.Equal(GetSensorValueTypedDataString(expected), actual.TypedData);
        }

        internal static void TestSensorEntity(SensorValueBase expected, SensorEntity actual)
        {
            Assert.NotNull(actual);
            Assert.False(string.IsNullOrEmpty(actual.Id));
            Assert.False(string.IsNullOrEmpty(actual.ProductId));
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Path, actual.DisplayName);
            Assert.Equal((int)GetSensorValueType(expected), actual.Type);
            Assert.Equal(default, actual.ExpectedUpdateIntervalTicks);
            Assert.Null(actual.Unit);
        }

        internal static void TestBarSensorFromUnitedSensor(UnitedSensorValue expected, BarSensorValueBase actual)
        {
            Assert.Equal(expected.Comment, actual.Comment);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.Key, actual.Key);
            Assert.Equal(expected.Time, actual.Time);

            switch (expected.Type)
            {
                case SensorType.IntegerBarSensor:
                    TestUnitedSensorValueData(expected, (IntBarSensorValue)actual);
                    break;
                case SensorType.DoubleBarSensor:
                    TestUnitedSensorValueData(expected, (DoubleBarSensorValue)actual);
                    break;
            }
        }


        private static void TestUnitedSensorValueData(UnitedSensorValue expected, DoubleBarSensorValue actual)
        {
            var barData = JsonSerializer.Deserialize<DoubleBarData>(expected.Data);

            Assert.Equal(barData.Min, actual.Min);
            Assert.Equal(barData.Max, actual.Max);
            Assert.Equal(barData.Mean, actual.Mean);
            Assert.Equal(barData.LastValue, actual.LastValue);
            Assert.Equal(barData.Count, actual.Count);
            Assert.Equal(barData.StartTime, actual.StartTime);
            Assert.Equal(barData.EndTime, actual.EndTime);

            Assert.Equal(barData.Percentiles.Count, actual.Percentiles.Count);
            for (int i = 0; i < barData.Percentiles.Count; ++i)
            {
                Assert.Equal(barData.Percentiles[i].Value, actual.Percentiles[i].Value);
                Assert.Equal(barData.Percentiles[i].Percentile, actual.Percentiles[i].Percentile);
            }
        }

        private static void TestUnitedSensorValueData(UnitedSensorValue expected, IntBarSensorValue actual)
        {
            var barData = JsonSerializer.Deserialize<IntBarData>(expected.Data);

            Assert.Equal(barData.Min, actual.Min);
            Assert.Equal(barData.Max, actual.Max);
            Assert.Equal(barData.Mean, actual.Mean);
            Assert.Equal(barData.LastValue, actual.LastValue);
            Assert.Equal(barData.Count, actual.Count);
            Assert.Equal(barData.StartTime, actual.StartTime);
            Assert.Equal(barData.EndTime, actual.EndTime);

            Assert.Equal(barData.Percentiles.Count, actual.Percentiles.Count);
            for (int i = 0; i < barData.Percentiles.Count; ++i)
            {
                Assert.Equal(barData.Percentiles[i].Value, actual.Percentiles[i].Value);
                Assert.Equal(barData.Percentiles[i].Percentile, actual.Percentiles[i].Percentile);
            }
        }


        internal static SensorType GetSensorValueType(SensorValueBase sensorValue) =>
           sensorValue switch
           {
               BoolSensorValue => SensorType.BooleanSensor,
               IntSensorValue => SensorType.IntSensor,
               DoubleSensorValue => SensorType.DoubleSensor,
               StringSensorValue => SensorType.StringSensor,
               IntBarSensorValue => SensorType.IntegerBarSensor,
               DoubleBarSensorValue => SensorType.DoubleBarSensor,
               FileSensorBytesValue => SensorType.FileSensorBytes,
               //UnitedSensorValue unitedSensorValue => unitedSensorValue.Type,
               _ => (SensorType)0,
           };


        internal static string GetSensorValueTypedDataString(SensorValueBase sensorValue) =>
            JsonSerializer.Serialize(GetSensorValueTypedData(sensorValue));

        private static object GetSensorValueTypedData(SensorValueBase sensorValue) =>
            sensorValue switch
            {
                BoolSensorValue boolSensorValue => GetSensorValueTypedData(boolSensorValue),
                IntSensorValue intSensorValue => GetSensorValueTypedData(intSensorValue),
                DoubleSensorValue doubleSensorValue => GetSensorValueTypedData(doubleSensorValue),
                StringSensorValue stringSensorValue => GetSensorValueTypedData(stringSensorValue),
                IntBarSensorValue intBarSensorValue => GetSensorValueTypedData(intBarSensorValue),
                DoubleBarSensorValue doubleBarSensorValue => GetSensorValueTypedData(doubleBarSensorValue),
                FileSensorBytesValue fileSensorBytesValue => GetSensorValueTypedData(fileSensorBytesValue),
                //UnitedSensorValue unitedSensorValue => GetSensorValueTypedData(unitedSensorValue),
                _ => null,
            };

        private static BoolSensorData GetSensorValueTypedData(BoolSensorValue sensorValue) =>
            GetBoolSensorData(sensorValue.BoolValue, sensorValue.Comment);

        private static IntSensorData GetSensorValueTypedData(IntSensorValue sensorValue) =>
            GetIntSensorData(sensorValue.IntValue, sensorValue.Comment);

        private static DoubleSensorData GetSensorValueTypedData(DoubleSensorValue sensorValue) =>
            GetDoubleSensorData(sensorValue.DoubleValue, sensorValue.Comment);

        private static StringSensorData GetSensorValueTypedData(StringSensorValue sensorValue) =>
            GetStringSensorData(sensorValue.StringValue, sensorValue.Comment);

        private static IntBarSensorData GetSensorValueTypedData(IntBarSensorValue sensorValue) =>
            GetIntBarSensorData(sensorValue.Min, sensorValue.Max, sensorValue.Mean, sensorValue.LastValue, sensorValue.Count,
                sensorValue.StartTime, sensorValue.EndTime, sensorValue.Percentiles, sensorValue.Comment);

        private static DoubleBarSensorData GetSensorValueTypedData(DoubleBarSensorValue sensorValue) =>
            GetDoubleBarSensorData(sensorValue.Min, sensorValue.Max, sensorValue.Mean, sensorValue.LastValue, sensorValue.Count,
                sensorValue.StartTime, sensorValue.EndTime, sensorValue.Percentiles, sensorValue.Comment);

        private static FileSensorBytesData GetSensorValueTypedData(FileSensorBytesValue sensorValue) =>
            GetFileSensorBytesData(sensorValue.Extension, sensorValue.FileName, sensorValue.FileContent, sensorValue.Comment);

        private static object GetSensorValueTypedData(UnitedSensorValue sensorValue) =>
            sensorValue.Type switch
            {
                SensorType.BooleanSensor => GetBoolSensorData(bool.Parse(sensorValue.Data), sensorValue.Comment),
                SensorType.IntSensor => GetIntSensorData(int.Parse(sensorValue.Data), sensorValue.Comment),
                SensorType.DoubleSensor => GetDoubleSensorData(double.Parse(sensorValue.Data), sensorValue.Comment),
                SensorType.StringSensor => GetStringSensorData(sensorValue.Data, sensorValue.Comment),
                SensorType.IntegerBarSensor => GetIntBarSensorData(sensorValue.Data, sensorValue.Comment),
                SensorType.DoubleBarSensor => GetDoubleBarSensorData(sensorValue.Data, sensorValue.Comment),
                _ => null,
            };

        private static BoolSensorData GetBoolSensorData(bool value, string comment) =>
            new()
            {
                BoolValue = value,
                Comment = comment,
            };

        private static IntSensorData GetIntSensorData(int value, string comment) =>
            new()
            {
                IntValue = value,
                Comment = comment,
            };

        private static DoubleSensorData GetDoubleSensorData(double value, string comment) =>
            new()
            {
                DoubleValue = value,
                Comment = comment,
            };

        private static StringSensorData GetStringSensorData(string value, string comment) =>
            new()
            {
                StringValue = value,
                Comment = comment,
            };

        private static IntBarSensorData GetIntBarSensorData(int min, int max, int mean, int lastValue, int count,
            DateTime startTime, DateTime endTime, List<PercentileValueInt> percentiles, string comment) =>
            new()
            {
                Comment = comment,
                Min = min,
                Max = max,
                Mean = mean,
                Percentiles = percentiles,
                Count = count,
                StartTime = startTime,
                EndTime = endTime,
                LastValue = lastValue,
            };

        private static IntBarSensorData GetIntBarSensorData(string data, string comment)
        {
            var intBarData = JsonSerializer.Deserialize<IntBarData>(data);

            return GetIntBarSensorData(intBarData.Min, intBarData.Max, intBarData.Mean, intBarData.LastValue,
                intBarData.Count, intBarData.StartTime, intBarData.EndTime, intBarData.Percentiles, comment);
        }

        private static DoubleBarSensorData GetDoubleBarSensorData(double min, double max, double mean, double lastValue, int count,
            DateTime startTime, DateTime endTime, List<PercentileValueDouble> percentiles, string comment) =>
            new()
            {
                Comment = comment,
                Min = min,
                Max = max,
                Mean = mean,
                Percentiles = percentiles,
                Count = count,
                StartTime = startTime,
                EndTime = endTime,
                LastValue = lastValue,
            };

        private static DoubleBarSensorData GetDoubleBarSensorData(string data, string comment)
        {
            var doubleBarData = JsonSerializer.Deserialize<DoubleBarSensorData>(data);

            return GetDoubleBarSensorData(doubleBarData.Min, doubleBarData.Max, doubleBarData.Mean, doubleBarData.LastValue,
                doubleBarData.Count, doubleBarData.StartTime, doubleBarData.EndTime, doubleBarData.Percentiles, comment);
        }

        private static FileSensorBytesData GetFileSensorBytesData(string extension, string filename, byte[] content, string comment) =>
            new()
            {
                Extension = extension,
                FileName = filename,
                FileContent = content,
                Comment = comment,
            };


        internal static void TestServerSensorValue(SensorValueBase expected, BaseValue actual)
        {
            Assert.NotEqual(DateTime.MinValue, actual.ReceivingTime);

            Assert.Equal(expected.Comment, actual.Comment);
            Assert.Equal(expected.Time, actual.Time);
            Assert.Equal(expected.Status.Convert(), actual.Status);
            Assert.Equal(expected.Type.Convert(), actual.Type);

            switch (expected)
            {
                case BoolSensorValue expectedBool:
                    TestSimpleValue(expectedBool, actual as BooleanValue);
                    break;
                case IntSensorValue expectedInt:
                    TestSimpleValue(expectedInt, actual as IntegerValue);
                    break;
                case DoubleSensorValue expectedDouble:
                    TestSimpleValue(expectedDouble, actual as DoubleValue);
                    break;
                case StringSensorValue expectedString:
                    TestSimpleValue(expectedString, actual as StringValue);
                    break;
                case FileSensorBytesValue expectedFile:
                    TestFileValue(expectedFile, actual as FileValue);
                    break;
                case IntBarSensorValue expectedIntBar:
                    var actualIntBar = actual as IntegerBarValue;

                    TestBarValue(expectedIntBar, actualIntBar);
                    TestPercentiles(expectedIntBar, actualIntBar);

                    break;
                case DoubleBarSensorValue expectedDoubleBar:
                    var actualDoubleBar = actual as DoubleBarValue;

                    TestBarValue(expectedDoubleBar, actualDoubleBar);
                    TestPercentiles(expectedDoubleBar, actualDoubleBar);

                    break;
            }
        }

        private static void TestSimpleValue<T>(ValueBase<T> expected, BaseValue<T> actual) =>
            Assert.Equal(expected.Value, actual.Value);

        private static void TestFileValue(FileSensorBytesValue expected, FileValue actual)
        {
            Assert.Equal(expected.FileName, actual.Name);
            Assert.Equal(expected.Extension, actual.Extension);
            Assert.Equal(expected.Value.LongLength, actual.OriginalSize);

            TestSimpleValue(expected, actual);
        }

        private static void TestBarValue<T>(BarValueSensorBase<T> expected, BarBaseValue<T> actual) where T : struct
        {
            Assert.Equal(expected.Count, actual.Count);
            Assert.Equal(expected.OpenTime, actual.OpenTime);
            Assert.Equal(expected.CloseTime, actual.CloseTime);
            Assert.Equal(expected.Min, actual.Min);
            Assert.Equal(expected.Max, actual.Max);
            Assert.Equal(expected.Mean, actual.Mean);
            Assert.Equal(expected.LastValue, actual.LastValue);
        }

        private static void TestPercentiles(DoubleBarSensorValue expected, DoubleBarValue actual)
        {
            var expectedDict = expected.Percentiles?.ToDictionary(k => k.Percentile, v => v.Value) ?? new();

            Assert.Equal(expectedDict, actual.Percentiles);
        }

        private static void TestPercentiles(IntBarSensorValue expected, IntegerBarValue actual)
        {
            var expectedDict = expected.Percentiles?.ToDictionary(k => k.Percentile, v => v.Value) ?? new();

            Assert.Equal(expectedDict, actual.Percentiles);
        }


        internal static void TestServerSensorValue(UnitedSensorValue expected, BaseValue actual)
        {
            Assert.NotEqual(DateTime.MinValue, actual.ReceivingTime);

            Assert.Equal(expected.Comment, actual.Comment);
            Assert.Equal(expected.Time, actual.Time);
            Assert.Equal(expected.Status.Convert(), actual.Status);
            Assert.Equal(expected.Type.Convert(), actual.Type);

            switch (expected.Type)
            {
                case SensorType.BooleanSensor:
                    Assert.Equal(bool.TryParse(expected.Data, out var boolValue) && boolValue, (actual as BooleanValue).Value);
                    break;
                case SensorType.IntSensor:
                    Assert.Equal(int.TryParse(expected.Data, out var intValue) ? intValue : 0, (actual as IntegerValue).Value);
                    break;
                case SensorType.DoubleSensor:
                    Assert.Equal(double.TryParse(expected.Data, out var doubleValue) ? doubleValue : 0, (actual as DoubleValue).Value);
                    break;
                case SensorType.StringSensor:
                    Assert.Equal(expected.Data, (actual as StringValue).Value);
                    break;
                case SensorType.IntegerBarSensor:
                    TestBarValue(expected, actual as IntegerBarValue);
                    break;
                case SensorType.DoubleBarSensor:
                    TestBarValue(expected, actual as DoubleBarValue);
                    break;
            }
        }

        private static void TestBarValue(UnitedSensorValue expected, IntegerBarValue actual)
        {
            var expectedBarData = JsonSerializer.Deserialize<IntBarData>(expected.Data);
            var expectedPercentilesDict = expectedBarData.Percentiles?.ToDictionary(k => k.Percentile, v => v.Value) ?? new();

            Assert.Equal(expectedBarData.Count, actual.Count);
            Assert.Equal(expectedBarData.StartTime.ToUniversalTime(), actual.OpenTime);
            Assert.Equal(expectedBarData.EndTime.ToUniversalTime(), actual.CloseTime);
            Assert.Equal(expectedBarData.Min, actual.Min);
            Assert.Equal(expectedBarData.Max, actual.Max);
            Assert.Equal(expectedBarData.Mean, actual.Mean);
            Assert.Equal(expectedBarData.LastValue, actual.LastValue);
            Assert.Equal(expectedPercentilesDict, actual.Percentiles);
        }

        private static void TestBarValue(UnitedSensorValue expected, DoubleBarValue actual)
        {
            var expectedBarData = JsonSerializer.Deserialize<DoubleBarData>(expected.Data);
            var expectedPercentilesDict = expectedBarData.Percentiles?.ToDictionary(k => k.Percentile, v => v.Value) ?? new();

            Assert.Equal(expectedBarData.Count, actual.Count);
            Assert.Equal(expectedBarData.StartTime.ToUniversalTime(), actual.OpenTime);
            Assert.Equal(expectedBarData.EndTime.ToUniversalTime(), actual.CloseTime);
            Assert.Equal(expectedBarData.Min, actual.Min);
            Assert.Equal(expectedBarData.Max, actual.Max);
            Assert.Equal(expectedBarData.Mean, actual.Mean);
            Assert.Equal(expectedBarData.LastValue, actual.LastValue);
            Assert.Equal(expectedPercentilesDict, actual.Percentiles);
        }


        private static Model.SensorType Convert(this SensorType type) =>
            type switch
            {
                SensorType.BooleanSensor => Model.SensorType.Boolean,
                SensorType.IntSensor => Model.SensorType.Integer,
                SensorType.DoubleSensor => Model.SensorType.Double,
                SensorType.StringSensor => Model.SensorType.String,
                SensorType.FileSensorBytes => Model.SensorType.File,
                SensorType.IntegerBarSensor => Model.SensorType.IntegerBar,
                SensorType.DoubleBarSensor => Model.SensorType.DoubleBar,
                _ => throw new NotImplementedException(),
            };

        private static SensorStatus Convert(this HSMSensorDataObjects.SensorStatus status) =>
          status switch
          {
              HSMSensorDataObjects.SensorStatus.Ok => SensorStatus.Ok,
              HSMSensorDataObjects.SensorStatus.Unknown => SensorStatus.Unknown,
              HSMSensorDataObjects.SensorStatus.Error => SensorStatus.Error,
              HSMSensorDataObjects.SensorStatus.Warning => SensorStatus.Warning,
              _ => SensorStatus.Unknown
          };
    }
}
