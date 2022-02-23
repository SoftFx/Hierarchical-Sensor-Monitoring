using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HSMServer.Core.Converters
{
    internal static class TypedDataFactory
    {
        internal static string GetTypedData(SensorValueBase sensorValue)
        {
            object sensorData = sensorValue switch
            {
                BoolSensorValue boolSensorValue => GetBoolSensorTypedData(boolSensorValue),
                IntSensorValue intSensorValue => GetIntSensorTypedData(intSensorValue),
                DoubleSensorValue doubleSensorValue => GetDoubleSensorTypedData(doubleSensorValue),
                StringSensorValue stringSensorValue => GetStringSensorTypedData(stringSensorValue),
                IntBarSensorValue intBarSensorValue => GetIntBarSensorTypedData(intBarSensorValue),
                DoubleBarSensorValue doubleBarSensorValue => GetDoubleBarSensorTypedData(doubleBarSensorValue),
                FileSensorBytesValue fileSensorBytesValue => GetFileSensorBytesTypedData(fileSensorBytesValue),
                UnitedSensorValue unitedSensorValue => GetUnitedSensorTypedData(unitedSensorValue),
                _ => null,
            };

            return sensorData != null ? JsonSerializer.Serialize(sensorData) : string.Empty;
        }


        private static BoolSensorData GetBoolSensorTypedData(BoolSensorValue sensorValue) =>
            GetSensorData(sensorValue.BoolValue, sensorValue.Comment);

        private static IntSensorData GetIntSensorTypedData(IntSensorValue sensorValue) =>
            GetSensorData(sensorValue.IntValue, sensorValue.Comment);

        private static DoubleSensorData GetDoubleSensorTypedData(DoubleSensorValue sensorValue) =>
            GetSensorData(sensorValue.DoubleValue, sensorValue.Comment);

        private static StringSensorData GetStringSensorTypedData(StringSensorValue sensorValue) =>
            GetSensorData(sensorValue.StringValue, sensorValue.Comment);

        private static IntBarSensorData GetIntBarSensorTypedData(IntBarSensorValue sensorValue) =>
            GetSensorData(sensorValue.Min, sensorValue.Max, sensorValue.Mean, sensorValue.LastValue,
                sensorValue.Count, sensorValue.StartTime, sensorValue.EndTime, sensorValue.Percentiles, sensorValue.Comment);

        private static DoubleBarSensorData GetDoubleBarSensorTypedData(DoubleBarSensorValue sensorValue) =>
            GetSensorData(sensorValue.Min, sensorValue.Max, sensorValue.Mean, sensorValue.LastValue,
                sensorValue.Count, sensorValue.StartTime, sensorValue.EndTime, sensorValue.Percentiles, sensorValue.Comment);

        private static FileSensorBytesData GetFileSensorBytesTypedData(FileSensorBytesValue sensorValue) =>
            GetSensorData(sensorValue.Extension, sensorValue.FileName, sensorValue.FileContent, sensorValue.Comment);

        private static object GetUnitedSensorTypedData(UnitedSensorValue sensorValue) =>
            sensorValue.Type switch
            {
                SensorType.BooleanSensor => GetSensorData(bool.Parse(sensorValue.Data), sensorValue.Comment),
                SensorType.IntSensor => GetSensorData(int.Parse(sensorValue.Data), sensorValue.Comment),
                SensorType.DoubleSensor => GetSensorData(double.Parse(sensorValue.Data), sensorValue.Comment),
                SensorType.StringSensor => GetSensorData(sensorValue.Data, sensorValue.Comment),
                SensorType.IntegerBarSensor => GetIntBarSensorData(sensorValue.Data, sensorValue.Comment),
                SensorType.DoubleBarSensor => GetDoubleBarSensorData(sensorValue.Data, sensorValue.Comment),
                _ => null,
            };


        private static BoolSensorData GetSensorData(bool value, string comment) =>
            new()
            {
                BoolValue = value,
                Comment = comment,
            };

        private static IntSensorData GetSensorData(int value, string comment) =>
            new()
            {
                IntValue = value,
                Comment = comment,
            };

        private static DoubleSensorData GetSensorData(double value, string comment) =>
            new()
            {
                DoubleValue = value,
                Comment = comment,
            };

        private static StringSensorData GetSensorData(string value, string comment) =>
            new()
            {
                StringValue = value,
                Comment = comment,
            };

        private static IntBarSensorData GetSensorData(int min, int max, int mean, int lastValue, int count,
            DateTime startTime, DateTime endTime, List<PercentileValueInt> percentiles, string comment) =>
            new()
            {
                Comment = comment,
                Min = min,
                Max = max,
                Mean = mean,
                Percentiles = percentiles,
                Count = count,
                StartTime = startTime.ToUniversalTime(),
                EndTime = (endTime == DateTime.MinValue ? DateTime.Now : endTime).ToUniversalTime(),
                LastValue = lastValue,
            };

        private static IntBarSensorData GetIntBarSensorData(string data, string comment)
        {
            var intBarData = JsonSerializer.Deserialize<IntBarData>(data);

            return GetSensorData(intBarData.Min, intBarData.Max, intBarData.Mean, intBarData.LastValue,
                intBarData.Count, intBarData.StartTime, intBarData.EndTime, intBarData.Percentiles, comment);
        }

        private static DoubleBarSensorData GetSensorData(double min, double max, double mean, double lastValue, int count,
            DateTime startTime, DateTime endTime, List<PercentileValueDouble> percentiles, string comment) =>
            new()
            {
                Comment = comment,
                Min = min,
                Max = max,
                Mean = mean,
                Percentiles = percentiles,
                Count = count,
                StartTime = startTime.ToUniversalTime(),
                EndTime = (endTime == DateTime.MinValue ? DateTime.Now : endTime).ToUniversalTime(),
                LastValue = lastValue,
            };

        private static DoubleBarSensorData GetDoubleBarSensorData(string data, string comment)
        {
            var intBarData = JsonSerializer.Deserialize<DoubleBarSensorData>(data);

            return GetSensorData(intBarData.Min, intBarData.Max, intBarData.Mean, intBarData.LastValue,
                intBarData.Count, intBarData.StartTime, intBarData.EndTime, intBarData.Percentiles, comment);
        }

        private static FileSensorBytesData GetSensorData(string extension, string filename, byte[] content, string comment) =>
            new()
            {
                Extension = extension,
                FileName = filename,
                FileContent = content,
                Comment = comment,
            };
    }
}
