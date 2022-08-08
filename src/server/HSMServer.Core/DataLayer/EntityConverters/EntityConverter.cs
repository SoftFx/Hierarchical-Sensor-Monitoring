using HSMServer.Core.Model;
using System.Text.Json;

namespace HSMServer.Core.DataLayer
{
    public static class EntityConverter
    {
        private const string SensorDataTypedDataPropertyName = "TypedData";


        public static BaseValue ConvertToSensorValue<T>(this byte[] entity) where T : BaseValue
        {
            if (entity == null)
                return null;

            var rootElement = JsonDocument.Parse(entity).RootElement;

            return rootElement.TryGetProperty(SensorDataTypedDataPropertyName, out _)
                ? SensorValuesFactory.BuildValue<T>(rootElement)
                : JsonSerializer.Deserialize<T>(rootElement);
        }
    }
}
