using System;
using System.Numerics;

namespace HSMServer.Core.Model
{
    public abstract record BarBaseValue : BaseValue
    {
        public int Count { get; init; }

        public DateTime OpenTime { get; init; }

        public DateTime CloseTime { get; init; }
    }


    public abstract record BarBaseValue<T> : BarBaseValue where T : struct, INumber<T>
    {
        public T Min { get; init; }

        public T Max { get; init; }

        public T Mean { get; init; }

        public T? FirstValue { get; init; }

        public T LastValue { get; init; }

        public override string ShortInfo =>
            $"Min = {Min}, Mean = {Mean}, Max = {Max}, Count = {Count}, First = {FirstValue}, Last = {LastValue}.";


        public override BaseValue TrySetValue(string str) => this;

        public override BaseValue TrySetValue(BaseValue value)
        {
            if (value is null)
                return this;

            var currValue = (BarBaseValue<T>)value;
            return this with
            {
                Min = currValue.Min,
                Max = currValue.Max,
                Count = currValue.Count,
                FirstValue = currValue.FirstValue,
                LastValue = currValue.LastValue,
                Mean = currValue.Mean,
            };
        }

        protected override bool IsEqual(BaseValue value) => false;
    }

    public sealed record NotCompressedValue<T> : BarBaseValue<T> where T : struct, INumber<T>
    {
        public bool IsCompressed { get; set; } = false;


        public NotCompressedValue(BarBaseValue<T> value, DateTime? time = null)
        {
            Count = value.Count;
            Max = value.Max;
            Min = value.Min;
            Mean = value.Mean;
            AggregatedValuesCount = value.AggregatedValuesCount;
            OpenTime = value.OpenTime;
            CloseTime = value.CloseTime;
            IsTimeout = value.IsTimeout;
            Comment = value.Comment;
            FirstValue = value.FirstValue;
            LastValue = value.LastValue;
            Status = value.Status;
            Time = time?.ToUniversalTime() ?? value.Time;
            ReceivingTime = value.ReceivingTime;
        }
    };
}
