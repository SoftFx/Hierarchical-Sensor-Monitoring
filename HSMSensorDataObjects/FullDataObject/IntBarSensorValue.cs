using System;
using System.Collections.Generic;
using HSMSensorDataObjects.BarData;

namespace HSMSensorDataObjects.FullDataObject
{
    public class IntBarSensorValue : SensorValueBase
    {
        public int Min { get; set; }
        public int Max { get; set; }
        public int Mean { get; set; }
        public List<PercentileValueInt> Percentiles { get; set; }
        public int Count { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        public IntBarSensorValue()
        {
            Percentiles = new List<PercentileValueInt>();
        }
    }
}
