using System;
using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public abstract class SensorValueBase : BaseRequest
    {
        public abstract SensorType Type { get; }

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
