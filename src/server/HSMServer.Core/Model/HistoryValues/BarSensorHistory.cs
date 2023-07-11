using System;
using System.Collections.Generic;

namespace HSMServer.Core.Model.HistoryValues
{
    public sealed class BarSensorHistory : SensorHistory
    {
        public DateTime OpenTime { get; init; }

        public DateTime CloseTime { get; init; }

        public int Count { get; init; }

        public string Min { get; init; }

        public string Max { get; init; }

        public string Mean { get; init; }

        public string LastValue { get; init; }

        public List<PercentileValue> Percentiles { get; init; }
    }


    public sealed class PercentileValue
    {
        public string Value { get; init; }

        public double Percentile { get; init; }
    }
}
