using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class IntSensorValue : SensorValueBase<int>
    {
        [DefaultValue((int)SensorType.IntSensor)]
        public override SensorType Type => SensorType.IntSensor;
    }
}
