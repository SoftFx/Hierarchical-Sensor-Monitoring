using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{ 
    [DataContract]
    public class IntSensorValue : ValueBase<int>
    {
        [Obsolete]
        public int IntValue 
        { 
            get => Value; 
            set { Value = value; IntValue = value;} 
        }
        [DataMember]
        public override SensorType Type { get => SensorType.IntSensor; }
    }
}
