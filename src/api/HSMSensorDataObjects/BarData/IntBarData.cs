using System;
using System.Collections.Generic;

namespace HSMSensorDataObjects.BarData
{
    [Obsolete("Use IntBarSensorValue")]
    public class IntBarData
    {
        public int LastValue { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public int Mean { get; set; }
        public int Count { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public Dictionary<double, int> Percentiles { get; set; }

        public IntBarData()
        {
            Percentiles = new Dictionary<double, int>();
        }
    }
}
