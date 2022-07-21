using System;
using System.Collections.Generic;

namespace HSMServer.Core.Model
{
    public abstract class BarValuesStorage<T> : ValuesStorage<T>, IDisposable where T : BarBaseValue
    {
        private T _lastValue;

        internal override BaseValue LastValue => _lastValue ?? base.LastValue;


        internal override T AddValue(T value)
        {
            var addedValue = _lastValue != null && _lastValue.OpenTime != value.OpenTime
                ? base.AddValue(_lastValue)
                : null;

            _lastValue = value;

            return addedValue;
        }

        internal override List<BaseValue> GetValues(int count)
        {
            if (_lastValue != null)
            {
                var values = base.GetValues(count - 1);
                values.Add(_lastValue);

                return values;
            }

            return base.GetValues(count);
        }

        internal override List<BaseValue> GetValues(DateTime from, DateTime to)
        {
            var values = base.GetValues(from, to);

            if (_lastValue != null && _lastValue.ReceivingTime >= from && _lastValue.ReceivingTime <= to)
                values.Add(_lastValue);

            return values;
        }


        public override void Dispose()
        {
            if (_lastValue != null)
                base.AddValue(_lastValue);
        }
    }
}
