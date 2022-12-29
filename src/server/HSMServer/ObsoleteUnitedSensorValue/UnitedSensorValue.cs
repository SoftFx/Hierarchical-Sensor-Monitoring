using HSMSensorDataObjects;
using System;
using System.ComponentModel;

namespace HSMServer.ObsoleteUnitedSensorValue
{
    [Obsolete("Remove this after removing supporting of DataCollector v2")]
    public class UnitedSensorValue
    {
        public string Key { get; set; }

        public string Path { get; set; }

        public DateTime Time { get; set; }

        public string Comment { get; set; }

        [DefaultValue((int)SensorStatus.Ok)]
        public SensorStatus Status { get; set; } = SensorStatus.Ok;

        public string Description { get; set; }

        public SensorType Type { get; set; }

        public string Data { get; set; }

        public bool IsBarSensor()
        {
            return Type == SensorType.DoubleBarSensor || Type == SensorType.IntegerBarSensor;
        }
    }
}
