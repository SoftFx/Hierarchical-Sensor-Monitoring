using System;
using System.Numerics;

namespace HSMServer.Datasources.Aggregators
{
    public readonly struct LinePointState<T> where T : INumber<T>
    {
        public DateTime Time { get; init; }

        public T Value { get; init; }


        public long Count { get; init; }


        public LinePointState(T value, DateTime time)
        {
            Value = value;
            Time = time;

            Count = 1;
        }


        public static LinePointState<T> GetMaxState(LinePointState<T> first, LinePointState<T> second)
        {
            var firstMax = first.Value > second.Value;

            return new()
            {
                Value = firstMax ? first.Value : second.Value,
                Time = firstMax ? first.Time : second.Time,
                Count = first.Count + second.Count,
            };
        }

        public static LinePointState<T> GetMinState(LinePointState<T> first, LinePointState<T> second)
        {
            var firstMin = first.Value < second.Value;

            return new()
            {
                Value = firstMin ? first.Value : second.Value,
                Time = firstMin ? first.Time : second.Time,
                Count = first.Count + second.Count,
            };
        }

        public static LinePointState<T> GetAvrState(LinePointState<T> first, LinePointState<T> second)
        {
            static double GetNumber(T value) => double.CreateChecked(value);

            var count = first.Count + second.Count;
            var oldCount = first.Count;

            return new()
            {
                Value = T.CreateChecked((GetNumber(first.Value) * oldCount + GetNumber(second.Value)) / count),
                Time = new DateTime((long)(((double)first.Time.Ticks * oldCount + second.Time.Ticks) / count), DateTimeKind.Utc),
                Count = count,
            };
        }
    }
}