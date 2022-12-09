using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class IntBarSensorValue : BarValueSensorBase<int>
    {
        [DataMember]
        [DefaultValue((int)SensorType.IntegerBarSensor)]
        public override SensorType Type => SensorType.IntegerBarSensor;
    }
}
