using System;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal class SummaryBarItem<T> where T : struct
    {
        public int Count { get; set; }

        public DateTime OpenTime { get; set; }

        public DateTime CloseTime { get; set; }

        public T Min { get; set; }

        public T Max { get; set; }

        public T Mean { get; set; }
    }
}
