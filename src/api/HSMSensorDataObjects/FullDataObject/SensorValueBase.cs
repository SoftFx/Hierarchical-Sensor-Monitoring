using System;
using System.ComponentModel;

namespace HSMSensorDataObjects.FullDataObject
{
    public abstract class SensorValueBase
    {
        public abstract SensorType Type { get; }

        public string Key { get; set; }

        public string Path { get; set; }

        public DateTime Time { get; set; } = DateTime.UtcNow;

        public string Comment { get; set; }

        [DefaultValue((int)SensorStatus.Ok)]
        public SensorStatus Status { get; set; } = SensorStatus.Ok;
    }


    public abstract class SensorValueBase<T> : SensorValueBase
    {
        public virtual T Value { get; set; }
    }
}
