using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMDataCollector.Sensors
{
    internal abstract class BaseFunctionSensorInstant<T> : MonitoringSensorBase<T>, IBaseFuncSensor
    {
        protected BaseFunctionSensorInstant(SensorOptions options) : base(options) { }


        TimeSpan IBaseFuncSensor.GetInterval() => PostTimePeriod;

        void IBaseFuncSensor.RestartTimer(TimeSpan timeSpan) => RestartTimer(timeSpan);


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


        public ValuesFunctionSensorInstant(Func<List<U>, T> getValue, ValuesFunctionSensorOptions options) : base(options)
        {
            _cacheSize = options.MaxCacheSize;
            _getValue = CheckFunc(getValue);
        }


        public void AddValue(U value)
        {
            _cache.Enqueue(value);

            while (_cache.Count > _cacheSize)
                if (!_cache.TryDequeue(out _))
                    break;
        }


        public Func<List<U>, T> GetFunc() => _getValue;

        protected override T GetValue() => _getValue.Invoke(CacheToList());


        private List<U> CacheToList()
        {
            var list = new List<U>(Math.Min(_cache.Count, _cacheSize));

            while (!_cache.IsEmpty)
            {
                if (_cache.TryDequeue(out var value))
                    list.Add(value);
            }

            return list;
        }
    }
}