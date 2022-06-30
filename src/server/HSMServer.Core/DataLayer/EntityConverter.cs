using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using System;
using System.Text.Json;

namespace HSMServer.Core.DataLayer
{
    public static class EntityConverter
    {
        private const string SensorNamePropertyName = "SensorName";
        private const string SensorTypePropertyName = "SensorType";

        private const string SensorDataTypedDataPropertyName = "TypedData";
        private const string SensorDataDataTypePropertyName = "DataType";


        // TODO: return SensorEntity with expectedupdateinterval is IsConverted = true
        public static SensorEntity ConvertSensorEntity(byte[] entity)
        {
            var jsonDocument = JsonDocument.Parse(entity);
            var rootElement = jsonDocument.RootElement;

            if (rootElement.TryGetProperty(nameof(SensorEntity.DisplayName), out _))
                return JsonSerializer.Deserialize<SensorEntity>(entity);

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

        public static BaseValue ConvertSensorData<T>(byte[] entity) where T : BaseValue, new()
        {
            var rootElement = JsonDocument.Parse(entity).RootElement;

            return rootElement.TryGetProperty(SensorDataTypedDataPropertyName, out _)
                ? new T().BuildValue(rootElement)
                : JsonSerializer.Deserialize<T>(rootElement);
        }

        internal static T GetProperty<T>(this JsonElement rootElement, string propertyName,
                                        Func<JsonElement, T> getPropertyAction, T defaultValue = default) =>
            rootElement.TryGetProperty(propertyName, out var property) ? getPropertyAction(property) : defaultValue;

        private static string GetStringProperty(JsonElement element) => element.GetString();

        private static byte GetByteProperty(JsonElement element) => element.GetByte();

        private static long GetLongProperty(JsonElement element) => element.GetInt64();

        private static byte GetSensorType(byte currentType) =>
            currentType == (byte)HSMSensorDataObjects.SensorType.FileSensorBytes ||
            currentType == (byte)HSMSensorDataObjects.SensorType.FileSensor
                ? (byte)SensorType.File
                : currentType;
    }
}
