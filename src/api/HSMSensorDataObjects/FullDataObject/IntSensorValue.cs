using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{ 
    [DataContract]
    public class IntSensorValue : ValueBase<int>
    {
        [DataMember]
        public override SensorType Type => SensorType.IntSensor;

        [Obsolete]
        public int IntValue 
        { 
            get => Value; 
            set 
            { 
                Value = value; 
                IntValue = value;
            } 
        }
    }
}
