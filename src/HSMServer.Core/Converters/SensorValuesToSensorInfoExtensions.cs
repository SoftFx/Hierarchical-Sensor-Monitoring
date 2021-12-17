using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.Converters
{
    public static class SensorValuesToSensorInfoExtensions
    {
        private const char SensorPathSeparator = '/';


        public static SensorInfo Convert(this SensorValueBase sensorValue, string productName) =>
            new()
            {
                Path = sensorValue.Path,
                Description = sensorValue.Description,
                ProductName = productName,
                SensorType = SensorTypeFactory.GetSensorType(sensorValue),
                SensorName = ExtractSensor(sensorValue.Path),
            };


        private static string ExtractSensor(string path) => path?.Split(SensorPathSeparator)?[^1];
    }
}
