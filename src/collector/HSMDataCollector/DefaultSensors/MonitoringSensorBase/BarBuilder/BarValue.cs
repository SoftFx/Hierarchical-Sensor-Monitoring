using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace HSMDataCollector.DefaultSensors.MonitoringSensorBase.BarBuilder
{
    internal readonly struct BarValue<T> where T: IComparable<T>
    {
        public T MaxValue { get; }
        public T MinValue { get; }
        public T LastValue { get; }
        public int Count { get; }
        public T Mean { get; }

        public BarValue(T value)
        {
            MaxValue = value;
            MinValue = value;
            LastValue = value;
            Mean = value;
            Count = 1;
        }

        private BarValue(T maxValue, T minValue, T lastValue, int count, T mean = default)
        {
            MaxValue = maxValue;
            MinValue = minValue;
            LastValue = lastValue;
            Count = count;
            Mean = mean;
        }

        public BarValue<T> AddValue(T value)
        {
            if (Count == 0)
                return new BarValue<T>(value);
            var maxValue = value.CompareTo(MaxValue) > 0 ? value : MaxValue;
            var minValue = value.CompareTo(MinValue) < 0 ? value : MinValue;
            return new BarValue<T>(maxValue, minValue, value, Count + 1);
        }

        public BarValue<T> Merge(BarValue<T> value)
        {
            if (Count == 0)
                return value;
            if (value.Count == 0)
                return this;
            var maxValue = value.MaxValue.CompareTo(MaxValue) > 0 ? value.MaxValue : MaxValue;
            var minValue = value.MinValue.CompareTo(MinValue) < 0 ? value.MinValue : MinValue;
            var count = value.Count + Count;
            return new BarValue<T>(maxValue, minValue, value.LastValue, count);
        }

        public BarValue<T> WithMean(T mean)
        {
            return new BarValue<T>(MaxValue, MinValue, LastValue, Count, mean);
        }
    }
}
