using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.Converters
{
    public static class SensorDataEntitiesExtensions
    {
        public static SensorData Convert(this SensorDataEntity dataEntity, SensorInfo sensorInfo, string productName)
        {
            var data = Convert(dataEntity, productName);
            data.Description = sensorInfo.Description;

            return data;
        }

        public static SensorData Convert(this SensorDataEntity dataEntity, string productName) =>
            new()
            {
                Path = dataEntity.Path,
                SensorType = (SensorType)dataEntity.DataType,
                Product = productName,
                Time = dataEntity.TimeCollected,
                StringValue = SensorDataPropertiesBuilder.GetStringValue(dataEntity),
                ShortStringValue = SensorDataPropertiesBuilder.GetShortStringValue(dataEntity),
                Status = (SensorStatus)dataEntity.Status
            };
    }
}
