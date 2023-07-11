using System;

namespace HSMServer.Core.Model.HistoryValues
{
    public abstract class SensorHistory
    {
        public string Comment { get; init; }

        public DateTime Time { get; init; }

        public string Status { get; init; }
    }
}
