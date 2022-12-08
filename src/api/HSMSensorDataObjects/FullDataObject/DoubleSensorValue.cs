using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class DoubleSensorValue : SensorValueBase<double>
    {
        [DataMember]
        [DefaultValue((int)SensorType.DoubleSensor)]
        public override SensorType Type => SensorType.DoubleSensor;
    }
}
