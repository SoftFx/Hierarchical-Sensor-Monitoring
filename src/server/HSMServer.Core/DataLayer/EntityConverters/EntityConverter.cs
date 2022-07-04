using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using System;
using System.Text.Json;

namespace HSMServer.Core.DataLayer
{
    public static class EntityConverter
    {
        private const string SensorIdPropertyName = "Id";
        private const string ProductIdPropertyName = "ProductId";
        private const string SensorNamePropertyName = "SensorName";
        private const string DescriptionPropertyName = "Description";
        private const string UnitPropertyName = "Unit";
        private const string SensorTypePropertyName = "SensorType";
        private const string ExpectedUpdateIntervalTicksPropertyName = "ExpectedUpdateIntervalTicks";

        private const string SensorDataTypedDataPropertyName = "TypedData";


        public static SensorEntity ConvertSensorEntity(byte[] entity)
        {
            var jsonDocument = JsonDocument.Parse(entity);
            var rootElement = jsonDocument.RootElement;

            if (rootElement.TryGetProperty(nameof(SensorEntity.DisplayName), out _))
                return JsonSerializer.Deserialize<SensorEntity>(entity);

            return new()
            {
                Id = rootElement.GetProperty(SensorIdPropertyName, GetStringProperty, Guid.NewGuid().ToString()),
                ProductId = rootElement.ReadString(ProductIdPropertyName),
                DisplayName = rootElement.ReadString(SensorNamePropertyName),
                Description = rootElement.ReadString(DescriptionPropertyName),
                Unit = rootElement.ReadString(UnitPropertyName),
                Type = GetSensorType(rootElement.ReadByte(SensorTypePropertyName)),
                ExpectedUpdateIntervalTicks = rootElement.ReadLong(ExpectedUpdateIntervalTicksPropertyName),
                IsConverted = true,
            };
        }

        public static BaseValue ConvertSensorData<T>(byte[] entity) where T : BaseValue
        {
            if (entity == null)
                return null;

            var rootElement = JsonDocument.Parse(entity).RootElement;

            return rootElement.TryGetProperty(SensorDataTypedDataPropertyName, out _)
                ? SensorValuesFactory.BuildValue<T>(rootElement)
                : JsonSerializer.Deserialize<T>(rootElement);
        }

        private static byte GetSensorType(byte currentType) =>
            currentType == (byte)HSMSensorDataObjects.SensorType.FileSensorBytes ||
            currentType == (byte)HSMSensorDataObjects.SensorType.FileSensor
                ? (byte)SensorType.File
                : currentType;

        private static string GetStringProperty(JsonElement element) => element.GetString();
    }
}
