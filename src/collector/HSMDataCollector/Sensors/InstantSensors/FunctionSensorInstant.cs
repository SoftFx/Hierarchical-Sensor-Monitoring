using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.Sensors
{
    internal abstract class BaseFunctionSensorInstant<T> : MonitoringSensorBase<T, NoDisplayUnit>, IBaseFuncSensor
    {
        protected BaseFunctionSensorInstant(MonitoringInstantSensorOptions options) : base(options) { }


        TimeSpan IBaseFuncSensor.GetInterval() => PostTimePeriod;

        void IBaseFuncSensor.RestartTimer(TimeSpan timeSpan) => _ = RestartTimerAsync(timeSpan);


        protected U CheckFunc<U>(U function)
        {
            if (function == null)
                throw new ArgumentNullException($"Custom function cannot be null. Sensor {SensorPath}");

            return function;
        }
    }


    internal sealed class FunctionSensorInstant<T> : BaseFunctionSensorInstant<T>, INoParamsFuncSensor<T>
    {
        private readonly Func<T> _getValue;


        public FunctionSensorInstant(Func<T> getValue, FunctionSensorOptions options) : base(options)
        {
            _getValue = CheckFunc(getValue);
        }


        public Func<T> GetFunc() => _getValue;

        protected override T GetValue() => _getValue.Invoke();
    }


    internal sealed class ValuesFunctionSensorInstant<T, U> : BaseFunctionSensorInstant<T>, IParamsFuncSensor<T, U>
    {
        private readonly ConcurrentQueue<U> _cache = new ConcurrentQueue<U>();
        private readonly Func<List<U>, T> _getValue;
        private readonly int _cacheSize;
        private int _cacheCount;


        public ValuesFunctionSensorInstant(Func<List<U>, T> getValue, ValuesFunctionSensorOptions options) : base(options)
        {
            if (options.MaxCacheSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxCacheSize), "Max cache size must be greater than zero.");

            _cacheSize = options.MaxCacheSize;
            _getValue = CheckFunc(getValue);
        }


        public void AddValue(U value)
        {
            _cache.Enqueue(value);
            Interlocked.Increment(ref _cacheCount);

            while (Volatile.Read(ref _cacheCount) > _cacheSize)
            {
                if (!_cache.TryDequeue(out _))
                    break;

                Interlocked.Decrement(ref _cacheCount);
            }
        }


        public Func<List<U>, T> GetFunc() => _getValue;

        protected override T GetValue() => _getValue.Invoke(_cache.ToList());


    }
}
