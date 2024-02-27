using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class CounterSensorValue : SensorValueBase<double>
    {
        [DefaultValue((int)SensorType.CounterSensor)]
        public override SensorType Type => SensorType.CounterSensor;
    }
}