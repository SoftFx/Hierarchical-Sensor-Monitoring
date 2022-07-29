using HSMSensorDataObjects.Swagger;
using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class IntSensorValue : ValueBase<int>
    {
        [DataMember]
        [DefaultValue((int)SensorType.IntSensor)]
        public override SensorType Type => SensorType.IntSensor;

        [Obsolete]
        [SwaggerExclude]
        public int IntValue
        {
            get => Value;
            set => Value = value;
        }
    }
}
