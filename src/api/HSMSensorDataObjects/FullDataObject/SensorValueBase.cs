﻿using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public abstract class SensorValueBase
    {
        [DataMember]
        public abstract SensorType Type { get; }

        [DataMember]
        public string Key { get; set; }

        [DataMember]
        public string Path { get; set; }

        [DataMember]
        public DateTime Time { get; set; } = DateTime.UtcNow;

        [DataMember]
        public string Comment { get; set; }

        [DataMember]
        [DefaultValue((int)SensorStatus.Ok)]
        public SensorStatus Status { get; set; } = SensorStatus.Ok;
    }


    [DataContract]
    public abstract class SensorValueBase<T> : SensorValueBase
    {
        [DataMember]
        public virtual T Value { get; set; }
    }
}
