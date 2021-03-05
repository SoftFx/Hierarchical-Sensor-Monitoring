using System.Runtime.Serialization;

namespace HSMSensorDataObjects.BarData
{
    [DataContract]
    public class PercentileValueInt
    {
        public PercentileValueInt()
        {
            Value = 0;
            Percentile = 0;
        }
        public PercentileValueInt(int value, double percentile)
        {
            Value = value;
            Percentile = percentile;
        }
        [DataMember]
        public int Value { get; set; }
        [DataMember]
        public double Percentile { get; set; }
    }
}