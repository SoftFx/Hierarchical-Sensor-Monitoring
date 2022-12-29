using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class IntBarSensorValue : BarSensorValueBase<int>
    {
        [DefaultValue((int)SensorType.IntegerBarSensor)]
        public override SensorType Type => SensorType.IntegerBarSensor;
    }
}
