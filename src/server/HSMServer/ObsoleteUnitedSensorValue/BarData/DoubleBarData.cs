using System;
using System.Collections.Generic;

namespace HSMServer.ObsoleteUnitedSensorValue
{
    [Obsolete("Remove this after removing supporting of DataCollector v2")]
    internal class DoubleBarData
    {
        public double LastValue { get; set; }

        public double Min { get; set; }

        public double Max { get; set; }

        public double Mean { get; set; }

        public int Count { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }

        public List<PercentileValueDouble> Percentiles { get; set; }
    }


    [Obsolete("Remove this after removing supporting of DataCollector v2")]
    internal class PercentileValueDouble
    {
        public double Value { get; set; }

        public double Percentile { get; set; }
    }
}
