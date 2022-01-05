using System.Text.Json;
using HSMSensorDataObjects;

namespace HSMServer.Core.Converters
{
    internal static class CommonSensorValuesToSensorValuesExtensions
    {
        internal static T Convert<T>(this CommonSensorValue sensorValue) =>
            JsonSerializer.Deserialize<T>(sensorValue.TypedValue);
    }
}
