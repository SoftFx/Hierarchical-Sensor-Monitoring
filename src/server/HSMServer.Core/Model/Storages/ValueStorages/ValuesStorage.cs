using HSMServer.Core.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace HSMServer.Core.Model
{
    public abstract class ValuesStorage
    {
        protected virtual int CacheSize => 100;


        internal SensorResult? Result => LastValue != null ? new SensorResult(LastValue) : null;


        internal abstract BaseValue LastTimeout { get; }

        internal abstract BaseValue LastDbValue { get; }

        internal abstract BaseValue LastValue { get; }

        internal abstract bool HasData { get; }


        internal abstract List<BaseValue> GetValues(DateTime from, DateTime to);

        internal abstract List<BaseValue> GetValues(int count);

        internal abstract bool TryChangeLastValue(BaseValue value);

        internal abstract BaseValue GetNewValue(BaseValue value, string newValue);

        internal abstract void Clear(DateTime to);

        internal abstract void Clear();
    }


    public abstract class ValuesStorage<T> : ValuesStorage where T : BaseValue
    {
        private readonly ConcurrentQueue<T> _cache = new();

        private T _lastValue, _lastTimeout;


        internal override T LastDbValue => _cache.LastOrDefault();

        internal override T LastTimeout => _lastTimeout;

        internal override T LastValue => _lastValue;

        internal override bool HasData => !_cache.IsEmpty;


        internal virtual void AddValue(T value) => AddValueBase(value);

        internal virtual void AddValueBase(T value)
        {
            if (value.IsTimeout)
            {
                if (_lastTimeout is null || _lastTimeout.ReceivingTime < value.ReceivingTime)
                    _lastTimeout = value;
            }
            else
            {
                _cache.Enqueue(value);

                if (_cache.Count > CacheSize)
                    _cache.TryDequeue(out _);

                if (_lastValue is null || value.Time >= _lastValue.Time)
                    _lastValue = value;
            }
        }

        internal override bool TryChangeLastValue(BaseValue value)
        {
            if (_cache.TryDequeue(out _) || _cache.IsEmpty)
            {
                AddValue((T)value);
                return true;
            }

            return false;
        }

        internal override BaseValue GetNewValue(BaseValue value, string newValue)
        {
            return value.Type switch
            {
                SensorType.Boolean => GetParsedBaseValue<BooleanValue, bool>(newValue, value),
                SensorType.Integer => GetParsedBaseValue<IntegerValue, int>(newValue, value),
                SensorType.Double => GetParsedBaseValue<DoubleValue, double>(newValue, value),
                SensorType.String => GetParsedBaseValue<StringValue, string>(newValue, value),
                SensorType.IntegerBar => (IntegerBarValue)value with { Max = 1, Min = 1, },
                SensorType.DoubleBar => (DoubleBarValue)value with { Max = 1, Min = 1, },
                SensorType.File => GetParsedBaseValue<FileValue, byte[]>(newValue, value),
                SensorType.TimeSpan => GetParsedBaseValue<TimeSpanValue, TimeSpan>(newValue, value),
                SensorType.Version => GetParsedBaseValue<VersionValue, Version>(newValue, value),
                _ => throw new ArgumentOutOfRangeException(nameof(value.Type))
            };


            BaseValue GetParsedBaseValue<TU, TK>(string value, BaseValue oldVal) where TU : BaseValue<TK>
            {
                var currentValue = (TU)oldVal;
                
                if (currentValue.TryParseValue(value, out var parsedValue))
                    currentValue = currentValue with
                    {
                        Value = parsedValue
                    };

                return currentValue;
            }
        }


        internal override List<BaseValue> GetValues(int count) =>
            _cache.Take(count).Select(v => (BaseValue)v).ToList();

        internal override List<BaseValue> GetValues(DateTime from, DateTime to) =>
            _cache.Where(v => v.InRange(from, to)).Select(u => (BaseValue)u).ToList();

        internal override void Clear(DateTime to)
        {
            while (_cache.FirstOrDefault()?.ReceivingTime <= to)
                _cache.TryDequeue(out _);

            if (_cache.IsEmpty)
                _lastValue = null;
        }

        internal override void Clear()
        {
            _cache.Clear();

            _lastValue = null;
        }
    }
}