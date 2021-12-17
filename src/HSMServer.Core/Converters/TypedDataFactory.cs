using System;
using System.Text.Json;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;

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
                FileSensorValue fileSensorValue => GetFileSensorTypedData(fileSensorValue),
                _ => null,
            };

            return JsonSerializer.Serialize(sensorData);
        }


        private static BoolSensorData GetBoolSensorTypedData(BoolSensorValue sensorValue) =>
            new()
            {
                BoolValue = sensorValue.BoolValue,
                Comment = sensorValue.Comment,
            };

        private static IntSensorData GetIntSensorTypedData(IntSensorValue sensorValue) =>
            new()
            {
                IntValue = sensorValue.IntValue,
                Comment = sensorValue.Comment,
            };

        private static DoubleSensorData GetDoubleSensorTypedData(DoubleSensorValue sensorValue) =>
            new()
            {
                DoubleValue = sensorValue.DoubleValue,
                Comment = sensorValue.Comment,
            };

        private static StringSensorData GetStringSensorTypedData(StringSensorValue sensorValue) =>
            new()
            {
                StringValue = sensorValue.StringValue,
                Comment = sensorValue.Comment,
            };

        private static IntBarSensorData GetIntBarSensorTypedData(IntBarSensorValue sensorValue) =>
            new()
            {
                Max = sensorValue.Max,
                Min = sensorValue.Min,
                Mean = sensorValue.Mean,
                LastValue = sensorValue.LastValue,
                Count = sensorValue.Count,
                Comment = sensorValue.Comment,
                StartTime = sensorValue.StartTime.ToUniversalTime(),
                EndTime = (sensorValue.EndTime == DateTime.MinValue ? DateTime.Now : sensorValue.EndTime).ToUniversalTime(),
                Percentiles = sensorValue.Percentiles,
            };

        private static DoubleBarSensorData GetDoubleBarSensorTypedData(DoubleBarSensorValue sensorValue) =>
            new()
            {
                Max = sensorValue.Max,
                Min = sensorValue.Min,
                Mean = sensorValue.Mean,
                LastValue = sensorValue.LastValue,
                Count = sensorValue.Count,
                Comment = sensorValue.Comment,
                StartTime = sensorValue.StartTime.ToUniversalTime(),
                EndTime = (sensorValue.EndTime == DateTime.MinValue ? DateTime.Now : sensorValue.EndTime).ToUniversalTime(),
                Percentiles = sensorValue.Percentiles,
            };

        private static FileSensorBytesData GetFileSensorBytesTypedData(FileSensorBytesValue sensorValue) =>
            new()
            {
                Comment = sensorValue.Comment,
                Extension = sensorValue.Extension,
                FileContent = sensorValue.FileContent,
                FileName = sensorValue.FileName,
            };

        private static FileSensorData GetFileSensorTypedData(FileSensorValue sensorValue) =>
            new()
            {
                Comment = sensorValue.Comment,
                Extension = sensorValue.Extension,
                FileContent = sensorValue.FileContent,
                FileName = sensorValue.FileName,
            };
    }
}
