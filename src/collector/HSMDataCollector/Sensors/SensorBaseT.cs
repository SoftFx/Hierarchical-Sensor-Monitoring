using System;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.SensorsFactory;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.DefaultSensors
{
    public abstract class SensorBase<T, TDisplayUnit> : SensorBase<TDisplayUnit> where TDisplayUnit : struct, Enum
    {
        private readonly Func<T, SensorValueBase> _valueBuilder;

        private T _current;

        public virtual T Current => _current;

        protected SensorBase(SensorOptions<TDisplayUnit> options) : base(options)
        {
            _valueBuilder = SensorValuesFactory.GetValueBuilder<T>(options.Type);
        }

        public void SendValue(T value, SensorStatus status = SensorStatus.Ok, string comment = "")
        {
            try
            {
                SendValue(GetSensorValue(value).Complete(comment, status));
            }
            catch (Exception ex) { HandleException(ex); }
        }


        protected virtual SensorValueBase GetSensorValue(T value)
        {
            _current = value;
            return value is SensorValueBase valueB ? valueB : _valueBuilder?.Invoke(value);
        }

    }
}