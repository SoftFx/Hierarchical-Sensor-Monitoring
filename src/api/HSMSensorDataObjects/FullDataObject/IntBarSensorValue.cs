using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HSMSensorDataObjects.BarData;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class IntBarSensorValue : BarValueBase<int>
    {
        public DateTime StartTime
        {
            get => OpenTime;
            set { OpenTime = value; StartTime = value; }
        }
        public DateTime EndTime
        {
            get => CloseTime;
            set { CloseTime = value; EndTime = value; }
        }
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
