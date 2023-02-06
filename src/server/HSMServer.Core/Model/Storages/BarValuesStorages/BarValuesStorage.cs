using System;
using System.Collections.Generic;

namespace HSMServer.Core.Model
{
    public abstract class BarValuesStorage<T> : ValuesStorage<T> where T : BarBaseValue
    {
        internal override bool HasData => LocalLastValue != default || base.HasData;

        internal override BaseValue LastValue => LocalLastValue ?? base.LastValue;

        internal T LocalLastValue { get; private set; }


        internal override T AddValue(T value)
        {
            var addedValue = LocalLastValue != null && LocalLastValue.OpenTime != value.OpenTime
                ? base.AddValue(LocalLastValue)
                : null;

            LocalLastValue = value;

            return addedValue;
        }

        internal override List<BaseValue> GetValues(int count)
        {
            if (LocalLastValue != null)
            {
                var values = base.GetValues(count - 1);
                values.Add(LocalLastValue);

                return values;
            }

            return base.GetValues(count);
        }

        internal override List<BaseValue> GetValues(DateTime from, DateTime to)
        {
            var values = base.GetValues(from, to);

            if (LocalLastValue != null && LocalLastValue.ReceivingTime >= from && LocalLastValue.ReceivingTime <= to)
                values.Add(LocalLastValue);

            return values;
        }

        internal override void Clear()
        {
            base.Clear();

            LocalLastValue = null;
        }
    }
}
