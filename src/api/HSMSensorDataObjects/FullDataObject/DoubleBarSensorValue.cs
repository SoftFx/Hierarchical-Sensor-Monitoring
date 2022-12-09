using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class DoubleBarSensorValue : BarValueSensorBase<double>
    {
        [DataMember]
        [DefaultValue((int)SensorType.DoubleBarSensor)]
        public override SensorType Type => SensorType.DoubleBarSensor;
    }
}
