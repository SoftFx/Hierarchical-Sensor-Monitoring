﻿using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.TypedDataObject
{
    [Obsolete("Use BoolSensorValue")]
    [DataContract]
    public class BoolSensorData
    {
        [DataMember]
        public string Comment { get; set; }
        [DataMember]
        public bool BoolValue { get; set; }
    }
}
