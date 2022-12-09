using System.ComponentModel;

namespace HSMSensorDataObjects.FullDataObject
{
    public class IntSensorValue : SensorValueBase<int>
    {
        [DefaultValue((int)SensorType.IntSensor)]
        public override SensorType Type => SensorType.IntSensor;
    }
}
