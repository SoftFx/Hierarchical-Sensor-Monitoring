using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.BarData
{
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

        public double Value { get; set; }

        public double Percentile { get; set; }
        //public void GetObjectData(SerializationInfo info, StreamingContext context)
        //{
        //    info.AddValue(nameof(Value), Value);
        //    info.AddValue(nameof(Percentile), Percentile);
        //}
    }
}