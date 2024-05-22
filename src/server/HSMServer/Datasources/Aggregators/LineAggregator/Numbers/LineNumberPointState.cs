using System;
using System.Numerics;

namespace HSMServer.Datasources.Aggregators
{
    public readonly struct LineNumberPointState<T> : ILinePointState<T>
        where T : INumber<T>
    {
        public DateTime Time { get; init; }

        public T Value { get; init; }


        public long Count { get; init; } = 1;


        public LineNumberPointState() { }


        public static LineNumberPointState<T> GetMaxState(LineNumberPointState<T> first, LineNumberPointState<T> second)
        {
            var firstMax = first.Value > second.Value;

            return new()
            {
                Value = firstMax ? first.Value : second.Value,
                Time = firstMax ? first.Time : second.Time,
                Count = first.Count + second.Count,
            };
        }

        public static LineNumberPointState<T> GetMinState(LineNumberPointState<T> first, LineNumberPointState<T> second)
        {
            var firstMin = first.Value < second.Value;

            return new()
            {
                Value = firstMin ? first.Value : second.Value,
                Time = firstMin ? first.Time : second.Time,
                Count = first.Count + second.Count,
            };
        }

        public static LineNumberPointState<T> GetAvrState(LineNumberPointState<T> first, LineNumberPointState<T> second)
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