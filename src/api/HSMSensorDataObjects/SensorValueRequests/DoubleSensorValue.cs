using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class DoubleSensorValue : SensorValueBase<double>
    {
        [DefaultValue((int)SensorType.DoubleSensor)]
        public override SensorType Type => SensorType.DoubleSensor;
    }
}
