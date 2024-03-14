using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class RateSensorValue : SensorValueBase<double>
    {
        [DefaultValue((int)SensorType.RateSensor)]
        public override SensorType Type => SensorType.RateSensor;
    }
}