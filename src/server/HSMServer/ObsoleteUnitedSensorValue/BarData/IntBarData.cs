using System;
using System.Collections.Generic;

namespace HSMServer.ObsoleteUnitedSensorValue
{
    [Obsolete("Remove this after removing supporting of DataCollector v2")]
    internal class IntBarData
    {
        public int LastValue { get; set; }

        public int Min { get; set; }

        public int Max { get; set; }

        public int Mean { get; set; }

        public int Count { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }

        public List<PercentileValueInt> Percentiles { get; set; }
    }


    [Obsolete("Remove this after removing supporting of DataCollector v2")]
    internal class PercentileValueInt
    {
        public int Value { get; set; }

        public double Percentile { get; set; }
    }
}
