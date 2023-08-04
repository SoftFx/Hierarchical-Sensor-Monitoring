using System;
using System.Collections.Generic;
using System.Numerics;

namespace HSMServer.Model.History
{
    internal sealed class SummaryBarItem<T> where T : INumber<T>
    {
        public int Count { get; set; }

        public DateTime OpenTime { get; set; }

        public DateTime CloseTime { get; set; }

        public T Min { get; set; }

        public T Max { get; set; }

        public T Mean { get; set; }

        public Dictionary<double, T> Percentiles { get; set; }


        public SummaryBarItem(DateTime openTime, DateTime closeTime, T max, T min)
        {
            OpenTime = openTime;
            CloseTime = closeTime;
            Max = max;
            Min = min;
        }
    }
}
