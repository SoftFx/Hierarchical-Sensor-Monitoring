using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class StringSensorValue : ValueBase<string>
    {
        public string StringValue 
        {
            get => Value; 
            set { Value = value; StringValue = value; }
        }
        [DataMember]
        public override SensorType Type { get => SensorType.StringSensor; }
    }
}
