using System;
using System.Collections.Generic;

namespace HSMServer.Core.Model
{
    public abstract class BarValuesStorage<T> : ValuesStorage<T> where T : BarBaseValue
    {
        private T _prevValue;


        internal override T LastValue => PartialLastValue ?? base.LastValue;

        internal override T LastDbValue => _prevValue;

        internal override bool HasData => PartialLastValue != default || base.HasData;


        internal T PartialLastValue { get; private set; }


        internal override void AddValue(T value)
        {
            var canStore = PartialLastValue != null && PartialLastValue.OpenTime != value.OpenTime;

            if (canStore)
            {
                _prevValue = PartialLastValue;

                base.AddValue(PartialLastValue);
            }

            PartialLastValue = value;
        }


        internal override List<BaseValue> GetValues(int count)
        {
            if (PartialLastValue != null)
            {
                var values = base.GetValues(count - 1);
                values.Add(PartialLastValue);

                return values;
            }

            return base.GetValues(count);
        }

        internal override List<BaseValue> GetValues(DateTime from, DateTime to)
        {
            var values = base.GetValues(from, to);

            if (PartialLastValue != null && PartialLastValue.ReceivingTime >= from && PartialLastValue.ReceivingTime <= to)
                values.Add(PartialLastValue);

            return values;
        }

        internal override void Clear()
        {
            base.Clear();

            PartialLastValue = null;
        }
    }
}
