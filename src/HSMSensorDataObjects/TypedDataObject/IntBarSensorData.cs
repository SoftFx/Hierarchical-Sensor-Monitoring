using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HSMSensorDataObjects.BarData;

namespace HSMSensorDataObjects.TypedDataObject
{
    [DataContract]
    public class IntBarSensorData
    {
        [DataMember]
        public string Comment { get; set; }
        [DataMember]
        public int Min { get; set; }
        [DataMember]
        public int Max { get; set; }
        [DataMember]
        public int Mean { get; set; }
        [DataMember]
        public List<PercentileValueInt> Percentiles { get; set; }
        [DataMember]
        public int Count { get; set; }
        [DataMember]
        public DateTime StartTime { get; set; }
        [DataMember]
        public DateTime EndTime { get; set; }
        [DataMember]
        public int LastValue { get; set; }
    }
}
