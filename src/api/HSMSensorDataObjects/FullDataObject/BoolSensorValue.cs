using HSMSensorDataObjects.Swagger;
using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class BoolSensorValue : ValueBase<bool>
    {
        [DataMember]
        [DefaultValue((int)SensorType.BooleanSensor)]
        public override SensorType Type => SensorType.BooleanSensor;

        [Obsolete]
        [SwaggerExclude]
        public bool BoolValue
        {
            get => Value;
            set => Value = value;
        }

        [DefaultValue(false)]
        public override bool Value 
        { 
            get => base.Value; 
            set => base.Value = value; 
        }
    }
}
