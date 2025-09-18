using HSMDataCollector.DefaultSensors;
using System;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMDataCollector.Options;


namespace HSMDataCollector.Sensors
{
    internal class SensorInstant<T, TDisplayUnit> : SensorBase<T, TDisplayUnit>, IInstantValueSensor<T> where TDisplayUnit : struct, Enum
    {
        public SensorInstant(SensorOptions<TDisplayUnit> options) : base(options) { }


        public virtual void AddValue(T value) => SendValue(value);

        public virtual void AddValue(T value, string comment = "") => SendValue(value, comment: comment);

        public virtual void AddValue(T value, SensorStatus status = SensorStatus.Ok, string comment = "") => SendValue(value, status, comment);
    }
}