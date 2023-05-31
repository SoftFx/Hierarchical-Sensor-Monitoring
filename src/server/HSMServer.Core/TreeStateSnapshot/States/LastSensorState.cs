using System;

namespace HSMServer.Core.TreeStateSnapshot
{
    public sealed class LastSensorState
    {
        public LastHistoryPeriod History { get; set; }
    }


    public sealed class LastHistoryPeriod
    {
        public DateTime From { get; set; } = DateTime.MinValue;

        public DateTime To { get; set; } = DateTime.MaxValue;
    }
}
