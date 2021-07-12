using System.Runtime.Serialization;

namespace HSMSensorDataObjects.BarData
{
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
        //public void GetObjectData(SerializationInfo info, StreamingContext context)
        //{
        //    info.AddValue(nameof(Value), Value);
        //    info.AddValue(nameof(Percentile), Percentile);
        //}
    }
}