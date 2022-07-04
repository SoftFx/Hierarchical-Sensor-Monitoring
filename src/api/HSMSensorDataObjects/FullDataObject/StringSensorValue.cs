using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class StringSensorValue : ValueBase<string>
    {
        [DataMember]
        public override SensorType Type => SensorType.StringSensor;

        [Obsolete]
        public string StringValue 
        {
            get => Value; 
            set 
            { 
                Value = value; 
                StringValue = value; 
            }
        }
    }
}
