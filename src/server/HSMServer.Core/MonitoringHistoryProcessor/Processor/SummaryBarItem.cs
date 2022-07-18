using HSMServer.Core.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal sealed class SummaryBarItem<T> where T : struct
    {
        public int Count { get; set; }

        public DateTime OpenTime { get; set; }

        public DateTime CloseTime { get; set; }

        public T Min { get; set; }

        public T Max { get; set; }

        public T Mean { get; set; }

        public Dictionary<double, T> Percentiles { get; set; }


        public SummaryBarItem(BarBaseValue<T> value)
        {
            Count = value.Count;
            Max = value.Max;
            Min = value.Min;
            OpenTime = value.OpenTime;
            CloseTime = value.CloseTime;
        }
    }
}
