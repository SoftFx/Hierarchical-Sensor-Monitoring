﻿using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class BoolSensorValue : ValueBase<bool>
    {
        [Obsolete]
        public bool BoolValue 
        { 
            get => Value;
            set { Value = value; BoolValue = value; }
        }
        [DataMember]
        public override SensorType Type { get => SensorType.BooleanSensor; }
    }
}
