using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class DoubleSensorValue : ValueBase<double>
    {
        public double DoubleValue 
        {
            get => Value;
            set { Value = value; DoubleValue = value; } 
        }
        [DataMember]
        public override SensorType Type { get => SensorType.DoubleSensor; }
    }
}
