using System;
using System.Collections.Generic;
using HSMSensorDataObjects.BarData;

namespace HSMSensorDataObjects.TypedDataObject
{
    public class DoubleBarSensorData
    {
        public string Comment { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Mean { get; set; }
        public int Count { get; set; }
        public List<PercentileValueDouble> Percentiles { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }
}
