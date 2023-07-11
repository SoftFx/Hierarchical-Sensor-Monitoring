using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class StringSensorValue : SensorValueBase<string>
    {
        [DefaultValue((int)SensorType.StringSensor)]
        public override SensorType Type => SensorType.StringSensor;
    }
}