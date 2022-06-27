using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Text.Json;

namespace HSMServer.Core.DataLayer
{
    public static class EntityConverter
    {
        private const string SensorNamePropertyName = "SensorName";
        private const string SensorTypePropertyName = "SensorType";


        // TODO: return SensorEntity with expectedupdateinterval is IsConverted = true
        public static SensorEntity ConvertSensorEntity(string entity)
        {
            var jsonDocument = JsonDocument.Parse(entity);
            var rootElement = jsonDocument.RootElement;

            if (rootElement.TryGetProperty(nameof(SensorEntity.DisplayName), out _))
                return JsonSerializer.Deserialize<SensorEntity>(entity);

            string GetStringProperty(JsonElement element) => element.GetString();
            byte GetByteProperty(JsonElement element) => element.GetByte();
            long GetLongProperty(JsonElement element) => element.GetInt64();

            return new()
            {
                Id = rootElement.GetProperty(nameof(SensorEntity.Id), GetStringProperty, Guid.NewGuid().ToString()),
                ProductId = rootElement.GetProperty(nameof(SensorEntity.ProductId), GetStringProperty),
                DisplayName = rootElement.GetProperty(SensorNamePropertyName, GetStringProperty),
                Description = rootElement.GetProperty(nameof(SensorEntity.Description), GetStringProperty),
                Unit = rootElement.GetProperty(nameof(SensorEntity.Unit), GetStringProperty),
                Type = GetSensorType(rootElement.GetProperty(SensorTypePropertyName, GetByteProperty)),
                ExpectedUpdateIntervalTicks = rootElement.GetProperty(nameof(SensorEntity.ExpectedUpdateIntervalTicks), GetLongProperty),
                IsConverted = true,
            };
        }

        private static T GetProperty<T>(this JsonElement rootElement, string propertyName,
                                        Func<JsonElement, T> getPropertyAction, T defaultValue = default) =>
            rootElement.TryGetProperty(propertyName, out var property) ? getPropertyAction(property) : defaultValue;

        private static byte GetSensorType(byte currentType) =>
            (HSMSensorDataObjects.SensorType)currentType == HSMSensorDataObjects.SensorType.FileSensorBytes ||
            (HSMSensorDataObjects.SensorType)currentType == HSMSensorDataObjects.SensorType.FileSensor
                ? (byte)Model.SensorType.File
                : currentType;
    }
}
