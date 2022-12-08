using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class BoolSensorValue : SensorValueBase<bool>
    {
        [DataMember]
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
