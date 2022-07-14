using System;

namespace HSMServer.Core.Model
{
    public abstract class BarValuesStorage<T> : ValuesStorage<T> where T : BarBaseValue , IDisposable
    {
        private T _lastValue;


        internal override T AddValue(T value)
        {
            if (_lastValue != null && _lastValue.OpenTime != value.OpenTime)
            {
                var addedValue = _lastValue;
                base.AddValue(_lastValue);
                _lastValue = value;

                return addedValue;
            }

            return null;
        }


        public void Dispose()
        {
            if (_lastValue != null) 
                base.AddValue(_lastValue);
        }
    }
}
