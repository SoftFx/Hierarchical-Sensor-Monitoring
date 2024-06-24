using System;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.SensorsFactory;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.DefaultSensors
{
    public abstract class SensorBase<T> : SensorBase
    {
        private readonly Func<T, SensorValueBase> _valueBuilder;


        protected SensorBase(SensorOptions options) : base(options)
        {
            _valueBuilder = SensorValuesFactory.GetValueBuilder<T>(options.Type);
        }


        public void SendValue(T value, SensorStatus status = SensorStatus.Ok, string comment = "")
        {
            SendValue(GetSensorValue(value).Complete(comment, status));
        }


        protected virtual SensorValueBase GetSensorValue(T value) => value is SensorValueBase valueB ? valueB : _valueBuilder?.Invoke(value);
    }
}