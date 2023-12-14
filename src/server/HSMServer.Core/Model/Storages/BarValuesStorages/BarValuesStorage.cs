using HSMServer.Core.Extensions;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Model
{
    public abstract class BarValuesStorage<T> : ValuesStorage<T> where T : BarBaseValue, new()
    {
        private T _prevValue;


        internal override T LastDbValue => _prevValue;

        internal override T LastValue => PartialLastValue ?? base.LastValue;

        internal override bool HasData => PartialLastValue != default || base.HasData;


        internal T PartialLastValue { get; private set; }


        internal override void AddValue(T value)
        {
            if (!value.IsTimeout)
            {
                var canStore = IsNewBar(value);

                if (canStore)
                {
                    _prevValue = PartialLastValue;
                    base.AddValue(PartialLastValue);
                }

                PartialLastValue = value;
            }
            else
                base.AddValue(value);
        }


        internal override List<BaseValue> GetValues(int count)
        {
            return PartialLastValue != null ? base.GetValues(count - 1).AddFluent(PartialLastValue)
                                            : base.GetValues(count);
        }

        internal override List<BaseValue> GetValues(DateTime from, DateTime to)
        {
            var values = base.GetValues(from, to);

            if (PartialLastValue?.InRange(from, to) ?? false)
                values.Add(PartialLastValue);

            return values;
        }

        internal override void Clear()
        {
            base.Clear();

            _prevValue = null;
            PartialLastValue = null;
        }

        internal override void Clear(DateTime to)
        {
            base.Clear(to);

            if (_prevValue?.ReceivingTime <= to)
                _prevValue = null;

            if (PartialLastValue?.ReceivingTime <= to)
                PartialLastValue = null;
        }


        protected T GetLastBar(T value) => IsNewBar(value) ? LastValue : LastDbValue;

        private bool IsNewBar(T value) => PartialLastValue != null && PartialLastValue.OpenTime != value.OpenTime;
    }
}
