using System.Collections.Generic;
using System.Runtime.Serialization;
using HSMSensorDataObjects.BarData;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class IntBarSensorValue : BarSensorValueBase
    {
        [DataMember]
        public int Min { get; set; }
        [DataMember]
        public int Max { get; set; }
        [DataMember]
        public int Mean { get; set; }
        [DataMember]
        public List<PercentileValueInt> Percentiles { get; set; }

        public IntBarSensorValue()
        {
            Percentiles = new List<PercentileValueInt>();
        }
    }
}
