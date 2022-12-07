using HSMServer.Core.Model;
using System.Text.Json;

namespace HSMServer.Core.DataLayer
{
    public static class EntityConverter
    {
        public static BaseValue ConvertToSensorValue<T>(this byte[] entity) where T : BaseValue
        {
            if (entity == null)
                return null;

            var rootElement = JsonDocument.Parse(entity).RootElement;

            return JsonSerializer.Deserialize<T>(rootElement);
        }
    }
}
