using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class DoubleSensorValue : ValueBase<double>
    {
        [DataMember]
        public override SensorType Type => SensorType.DoubleSensor;

        [Obsolete]
        public double DoubleValue 
        {
            get => Value;
            set { Value = value; DoubleValue = value; } 
        }
    }
}
