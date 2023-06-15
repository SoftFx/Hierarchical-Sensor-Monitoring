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
    }


    public class SimpleSensorValueViewModel : TableValueViewModel
    {
        public string Value { get; init; }
    }


    public class BarSensorValueViewModel : TableValueViewModel
    {
        public int Count { get; init; }

        public string Min { get; init; }

        public string Max { get; init; }

        public string Mean { get; init; }

        public string LastValue { get; init; }
    }
}
