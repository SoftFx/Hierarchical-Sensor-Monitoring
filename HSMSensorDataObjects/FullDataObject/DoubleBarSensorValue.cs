using System.Collections.Generic;
using System.Runtime.Serialization;
using HSMSensorDataObjects.BarData;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class DoubleBarSensorValue : BarSensorValueBase
    {
        [DataMember]
        public double Min { get; set; }
        [DataMember]
        public double Max { get; set; }
        [DataMember]
        public double Mean { get; set; }
        [DataMember]
        public List<PercentileValueDouble> Percentiles { get; set; }

        public DoubleBarSensorValue()
        {
            Percentiles = new List<PercentileValueDouble>();
        }
    }
}
