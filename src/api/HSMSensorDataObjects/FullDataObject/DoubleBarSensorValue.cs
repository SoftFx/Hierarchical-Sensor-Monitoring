using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HSMSensorDataObjects.BarData;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class DoubleBarSensorValue : BarValueSensorBase<double>
    {
        [DataMember]
        public override SensorType Type { get => SensorType.DoubleBarSensor; }
        [DataMember]
        public List<PercentileValueDouble> Percentiles { get; set; }


        public DoubleBarSensorValue()
        {
            Percentiles = new List<PercentileValueDouble>();
        }
    }
}
