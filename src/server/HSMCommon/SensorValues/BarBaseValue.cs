using System;
using System.Numerics;
using MemoryPack;


namespace HSMCommon.Model
{
    [MemoryPackable]
    [MemoryPackUnion(10, typeof(IntegerBarValue))]
    [MemoryPackUnion(11, typeof(DoubleBarValue))]
    public abstract partial record BarBaseValue : BaseValue
    {
        public int Count { get; init; }

        public DateTime OpenTime { get; init; }

        public DateTime CloseTime { get; init; }

        public double? EmaMin { get; init; }

        public double? EmaMax { get; init; }

        public double? EmaMean { get; init; }

        public double? EmaCount { get; init; }


        public bool IsUpdatedBar(BarBaseValue newBar) => OpenTime == newBar?.OpenTime && CloseTime == newBar?.CloseTime;
    }

    public abstract partial record BarBaseValue<T> : BarBaseValue where T : struct, INumber<T>
    {
        public T Min { get; init; }

        public T Max { get; init; }

        public T Mean { get; init; }

        public T? FirstValue { get; init; }

        public T LastValue { get; init; }

        /// <summary>
        /// Population standard deviation of the bar's samples (#1509); <c>null</c> = unknown (a
        /// collector that does not send it, a row stored before it existed, a bar built from
        /// pre-aggregated partials). Unknown is never 0. MemoryPack serializes members in
        /// declaration order with a member count, so this field MUST stay the last member of the
        /// bar records: a row written before it existed deserializes with it null. Forward-only: a
        /// server built without this member cannot read rows written with it (MemoryPack's default
        /// layout throws on extra members), so a rollback needs a pre-upgrade database backup.
        /// </summary>
        public double? StdDev { get; init; }


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
                StdDev = currValue.StdDev,
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
            StdDev = value.StdDev;
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
