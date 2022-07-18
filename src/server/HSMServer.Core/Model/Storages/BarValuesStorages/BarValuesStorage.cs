using System;

namespace HSMServer.Core.Model
{
    public abstract class BarValuesStorage<T> : ValuesStorage<T>, IDisposable where T : BarBaseValue
    {
        private T _lastValue;


        internal override T AddValue(T value)
        {
            if (_lastValue == null)
            {
                if (value.CloseTime == DateTime.MinValue)
                {
                    _lastValue = value;
                    return null;
                }
                else 
                    return base.AddValue(value);
            }         
            else
            {
                var addedValue = _lastValue with 
                {
                    CloseTime = DateTime.UtcNow 
                };
                _lastValue = value;

                return base.AddValue(addedValue);
            }
        }


        public override void Dispose()
        {
            if (_lastValue != null) 
                base.AddValue(_lastValue);
        }
    }
}
