using System;
using System.Collections.Generic;

namespace HSMSensorDataObjects.BarData
{
    [Obsolete("Use DoubleBarSensorValue")]
    public class DoubleBarData
    {
        public double LastValue { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Mean { get; set; }
        public int Count { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<PercentileValueDouble> Percentiles { get; set; }

        public DoubleBarData()
        {
            Percentiles = new List<PercentileValueDouble>();
        }
    }
}
