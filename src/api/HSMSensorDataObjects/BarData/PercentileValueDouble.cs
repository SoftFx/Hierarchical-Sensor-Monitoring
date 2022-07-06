using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.BarData
{
    [Obsolete]
    [DataContract]
    public class PercentileValueDouble
    {
        public PercentileValueDouble()
        {
            Value = 0;
            Percentile = 0;
        }
        public PercentileValueDouble(double value, double percentile)
        {
            Value = value;
            Percentile = percentile;
        }
        [DataMember]
        public double Value { get; set; }
        [DataMember]
        public double Percentile { get; set; }
    }
}