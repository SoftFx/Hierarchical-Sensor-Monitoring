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
        public static SensorEntity ConvertSensorEntity(string oldEntity)
        {
            var jsonDocument = JsonDocument.Parse(oldEntity);
            var rootElement = jsonDocument.RootElement;

            if (rootElement.TryGetProperty(nameof(SensorEntity.DisplayName), out _))
                return JsonSerializer.Deserialize<SensorEntity>(oldEntity);

            string GetStringProperty(JsonElement element) => element.GetString();
            byte GetByteProperty(JsonElement element) => element.GetByte();

            return new()
            {
                Id = rootElement.GetProperty(nameof(SensorEntity.Id), GetStringProperty, Guid.NewGuid().ToString()),
                ProductId = rootElement.GetProperty(nameof(SensorEntity.ProductId), GetStringProperty),
                DisplayName = rootElement.GetProperty(SensorNamePropertyName, GetStringProperty),
                Description = rootElement.GetProperty(nameof(SensorEntity.Description), GetStringProperty),
                Unit = rootElement.GetProperty(nameof(SensorEntity.Unit), GetStringProperty),
                Type = rootElement.GetProperty(SensorTypePropertyName, GetByteProperty),
                IsConverted = true,
            };
        }

        private static T GetProperty<T>(this JsonElement rootElement, string propertyName,
                                        Func<JsonElement, T> getPropertyAction, T defaultValue = default) =>
            rootElement.TryGetProperty(propertyName, out var property) ? getPropertyAction(property) : defaultValue;
    }
}
