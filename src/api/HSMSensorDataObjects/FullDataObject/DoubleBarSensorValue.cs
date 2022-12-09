using System.ComponentModel;

namespace HSMSensorDataObjects.FullDataObject
{
    public class DoubleBarSensorValue : BarValueSensorBase<double>
    {
        [DefaultValue((int)SensorType.DoubleBarSensor)]
        public override SensorType Type => SensorType.DoubleBarSensor;
    }
}
