using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class BoolSensorValue : SensorValueBase<bool>
    {
        [DefaultValue((int)SensorType.BooleanSensor)]
        public override SensorType Type => SensorType.BooleanSensor;

        [DefaultValue(false)]
        public override bool Value
        {
            get => base.Value;
            set => base.Value = value;
        }
    }
}
