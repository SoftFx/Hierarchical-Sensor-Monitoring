using System.ComponentModel;

namespace HSMSensorDataObjects.FullDataObject
{
    public class StringSensorValue : SensorValueBase<string>
    {
        [DefaultValue((int)SensorType.StringSensor)]
        public override SensorType Type => SensorType.StringSensor;
    }
}