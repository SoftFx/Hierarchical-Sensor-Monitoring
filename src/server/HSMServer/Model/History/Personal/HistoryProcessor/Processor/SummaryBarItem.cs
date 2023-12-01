using System;
using System.Numerics;

namespace HSMServer.Model.History
{
    internal sealed class SummaryBarItem<T> where T : struct, INumber<T>
    {
        public int Count { get; set; }

        public DateTime OpenTime { get; set; }

        public DateTime CloseTime { get; set; }

        public T Min { get; set; }

        public T Max { get; set; }

        public T Mean { get; set; }

        public T? FirstValue { get; set; }

        public T LastValue { get; set; }


        public SummaryBarItem(DateTime openTime, DateTime closeTime, T max, T min, T? firstValue, T lastValue)
        {
            FirstValue = firstValue;
            LastValue = lastValue;
            OpenTime = openTime;
            CloseTime = closeTime;
            Max = max;
            Min = min;
        }
    }
}
