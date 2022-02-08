using HSMSensorDataObjects;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal sealed class SensorInfoFactory
    {
        internal static SensorInfo BuildSensorInfo(string productName, byte sensorType) =>
            new()
            {
                Path = nameof(SensorInfo),
                ProductName = productName,
                SensorName = nameof(SensorInfo),
                Description = $"{nameof(SensorInfo)} {nameof(SensorInfo.Description)}",
                SensorType = (SensorType)sensorType,
                Unit = RandomGenerator.GetRandomString(),
            };
    }
}
