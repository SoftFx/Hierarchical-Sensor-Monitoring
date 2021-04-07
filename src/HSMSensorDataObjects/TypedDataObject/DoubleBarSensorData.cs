using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HSMSensorDataObjects.BarData;

namespace HSMSensorDataObjects.TypedDataObject
{
    [DataContract]
    public class DoubleBarSensorData
    {
        [DataMember]
        public string Comment { get; set; }
        [DataMember]
        public double Min { get; set; }
        [DataMember]
        public double Max { get; set; }
        [DataMember]
        public double Mean { get; set; }
        [DataMember]
        public int Count { get; set; }
        [DataMember]
        public List<PercentileValueDouble> Percentiles { get; set; }
        [DataMember]
        public DateTime StartTime { get; set; }
        [DataMember]
        public DateTime EndTime { get; set; }
    }
}
