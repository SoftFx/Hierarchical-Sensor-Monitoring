using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HSMSensorDataObjects.BarData;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class IntBarSensorValue : BarValueSensorBase<int>
    {
        [DataMember]
        public override SensorType Type { get => SensorType.IntegerBarSensor; }
        [DataMember]
        public List<PercentileValueInt> Percentiles { get; set; }


        public IntBarSensorValue()
        {
            Percentiles = new List<PercentileValueInt>();
        }
    }
}
