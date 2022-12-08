using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class StringSensorValue : SensorValueBase<string>
    {
        [DataMember]
        [DefaultValue((int)SensorType.StringSensor)]
        public override SensorType Type => SensorType.StringSensor;
    }
}