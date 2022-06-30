using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HSMSensorDataObjects.BarData;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class DoubleBarSensorValue : BarValueBase<double>
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
        public override SensorType Type { get => SensorType.DoubleBarSensor; }
        [DataMember]
        public List<PercentileValueDouble> Percentiles { get; set; }


        public DoubleBarSensorValue()
        {
            Percentiles = new List<PercentileValueDouble>();
        }
    }
}
