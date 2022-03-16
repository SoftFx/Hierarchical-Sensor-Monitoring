using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Model.Sensor;
using System.Text;
using System.Text.Json;

namespace HSMServer.Core.Converters
{
    public static class FileSensorExtensions
    {
        public static FileSensorBytesValue ConvertToFileSensorBytes(this FileSensorValue sensorValue) =>
            new()
            {
                Key = sensorValue.Key,
                Path = sensorValue.Path,
                Time = sensorValue.Time,
                Comment = sensorValue.Comment,
                Status = sensorValue.Status,
                Description = sensorValue.Description,
                Extension = sensorValue.Extension,
                FileContent = Encoding.UTF8.GetBytes(sensorValue.FileContent),
                FileName = sensorValue.FileName,
            };

        public static SensorHistoryData ConvertToFileSensorBytes(this SensorHistoryData sensorData)
        {
            if (sensorData.SensorType != SensorType.FileSensor)
                return sensorData;

            return new()
            {
                Time = sensorData.Time,
                SensorType = SensorType.FileSensorBytes,
                TypedData = GetTypedDataForFileSensorBytes(sensorData.TypedData),
                OriginalFileSensorContentSize = sensorData.OriginalFileSensorContentSize,
            };
        }

        internal static SensorDataEntity ConvertToFileSensorBytes(this SensorDataEntity dataEntity)
        {
            if (dataEntity.DataType != (byte)SensorType.FileSensor)
                return dataEntity;

            return new()
            {
                Path = dataEntity.Path,
                Status = dataEntity.Status,
                Time = dataEntity.Time,
                Timestamp = dataEntity.Timestamp,
                TimeCollected = dataEntity.TimeCollected,
                DataType = (byte)SensorType.FileSensorBytes,
                TypedData = GetTypedDataForFileSensorBytes(dataEntity.TypedData),
                OriginalFileSensorContentSize = dataEntity.OriginalFileSensorContentSize,
            };
        }

        private static string GetTypedDataForFileSensorBytes(string fileSensorTypedData)
        {
            var fileSensorData = JsonSerializer.Deserialize<FileSensorData>(fileSensorTypedData);
            var fileSensorBytesData = new FileSensorBytesData()
            {
                Comment = fileSensorData.Comment,
                Extension = fileSensorData.Extension,
                FileContent = Encoding.UTF8.GetBytes(fileSensorData.FileContent),
                FileName = fileSensorData.FileName,
            };

            return JsonSerializer.Serialize(fileSensorBytesData);
        }
    }
}
