using System;

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

        // Population standard deviation (#1509); null when unknown (never "0").
        public string StdDev { get; init; }

        public string FirstValue { get; init; }

        public string LastValue { get; init; }
    }
}
