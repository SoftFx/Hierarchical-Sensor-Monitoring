using System;
using SensorStatus = HSMServer.Model.TreeViewModel.SensorStatus;

namespace HSMServer.Model.History
{
    public abstract class TableValueViewModel
    {
        public DateTime Time { get; init; }

        public string Comment { get; init; }

        public SensorStatus Status { get; init; }

        public DateTime ReceivingTime { get; init; }

        public required bool IsTimeout { get; init; }
    }


    public class SimpleSensorValueViewModel : TableValueViewModel
    {
        public long AggregatedValuesCount { get; init; }

        public DateTime LastUpdateTime { get; init; }

        public string Value { get; init; }
    }


    public class BarSensorValueViewModel : TableValueViewModel
    {
        public DateTime OpenTime { get; init; }

        public DateTime CloseTime { get; init; }

        public int Count { get; init; }

        public string Min { get; init; }

        public string Max { get; init; }

        public string Mean { get; init; }

        public string LastValue { get; init; }
    }
}
