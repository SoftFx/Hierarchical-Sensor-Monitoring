using HSMSensorDataObjects.Swagger;
using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class DoubleSensorValue : ValueBase<double>
    {
        [DataMember]
        [DefaultValue((int)SensorType.DoubleSensor)]
        public override SensorType Type => SensorType.DoubleSensor;

        [Obsolete]
        [SwaggerExclude]
        public double DoubleValue
        {
            get => Value;
            set => Value = value;
        }
    }
}
