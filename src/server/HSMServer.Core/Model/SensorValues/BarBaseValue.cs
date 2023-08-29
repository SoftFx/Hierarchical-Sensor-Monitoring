using System;
using System.Collections.Generic;
using System.Numerics;

namespace HSMServer.Core.Model
{
    public abstract record BarBaseValue : BaseValue
    {
        public int Count { get; init; }

        public DateTime OpenTime { get; init; }

        public DateTime CloseTime { get; init; }
    }


    public abstract record BarBaseValue<T> : BarBaseValue where T : INumber<T>
    {
        public Dictionary<double, T> Percentiles { get; init; } = new();


        public T Min { get; init; }

        public T Max { get; init; }

        public T Mean { get; init; }

        public T LastValue { get; init; }


        public override BaseValue TrySetValue(string str) => this;

        public override string ShortInfo =>
            $"Min = {Min}, Mean = {Mean}, Max = {Max}, Count = {Count}, Last = {LastValue}.";


        protected override bool IsEqual(BaseValue value) => false;
    }
}
