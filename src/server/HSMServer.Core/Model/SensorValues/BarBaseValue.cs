using System;

namespace HSMServer.Core.Model
{
    public abstract record BarBaseValue : BaseValue
    {
        public int Count { get; init; }

        public DateTime OpenTime { get; init; }

        public DateTime CloseTime { get; init; }
    }


    public abstract record BarBaseValue<T> : BarBaseValue where T : struct
    {
        public T Min { get; init; }

        public T Max { get; init; }

        public T Mean { get; init; }

        public T LastValue { get; init; }


        public override string ToString() =>
            $"Min = {Min}, Mean = {Mean}, Max = {Max}, Count = {Count}, Last = {LastValue}.";
    }
}
