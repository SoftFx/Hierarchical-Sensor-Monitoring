using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class DoubleBarSensorValue : BarSensorValueBase<double>
    {
        [DefaultValue((int)SensorType.DoubleBarSensor)]
        public override SensorType Type => SensorType.DoubleBarSensor;
    }
}
