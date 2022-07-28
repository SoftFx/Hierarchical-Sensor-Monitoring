using HSMSensorDataObjects.Swagger;
using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class StringSensorValue : ValueBase<string>
    {
        [DataMember]
        [DefaultValue((int)SensorType.StringSensor)]
        public override SensorType Type => SensorType.StringSensor;

        [Obsolete]
        [SwaggerExclude]
        public string StringValue
        {
            get => Value;
            set => Value = value;
        }
    }
}