using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class DoubleSensorValue : ValueBase<double>
    {
        [Obsolete]
        public double DoubleValue 
        {
            get => Value;
            set { Value = value; DoubleValue = value; } 
        }
        [DataMember]
        public override SensorType Type { get => SensorType.DoubleSensor; }
    }
}
