using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class IntSensorValue : SensorValueBase<int>
    {
        [DataMember]
        [DefaultValue((int)SensorType.IntSensor)]
        public override SensorType Type => SensorType.IntSensor;
    }
}
