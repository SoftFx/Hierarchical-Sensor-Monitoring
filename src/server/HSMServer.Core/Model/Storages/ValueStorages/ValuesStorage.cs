using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Extensions;
using HSMCommon.Model;
using HSMServer.Core.Extensions;


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

        internal abstract DateTime From { get; }

        internal abstract DateTime To { get; }

        internal abstract List<BaseValue> GetValues(DateTime from, DateTime to);

        internal abstract List<BaseValue> GetValues(int count);

        internal abstract bool TryChangeLastValue(BaseValue value);

        internal abstract void Clear(DateTime to);

        internal abstract void Clear();

        internal abstract void Cut(DateTime time);

    }


    public abstract class ValuesStorage<T> : ValuesStorage where T : BaseValue, new()
    {
        private readonly ConcurrentQueue<T> _cache = new();
        private readonly TimeSpan _singletonTimePrecision = TimeSpan.FromSeconds(1);

        private T _lastValue, _lastTimeout;

        DateTime _from = DateTime.MinValue;
        DateTime _to   = DateTime.MaxValue;

        // Activity floor restored by a history-load retry (#1344), kept apart from _to so the
        // "_to is stamped together with _lastValue and a _cache enqueue" pairing of AddValueBase
        // stays intact: a retried sensor must not look like one that has a cached last value.
        // MinValue = no floor restored.
        DateTime _lastActivity = DateTime.MinValue;

        private bool IsLastEmptyOrTimeout => LastValue is null || LastTimeout?.ReceivingTime > LastValue.ReceivingTime;

        internal override T LastDbValue => _cache.LastOrDefault();

        internal override T LastTimeout => _lastTimeout;

        internal override T LastValue => _lastValue;

        internal override bool HasData => !_cache.IsEmpty;

        internal override DateTime From => _from;

        // Newest of the ingestion stamp and the retry-restored floor; MaxValue keeps its
        // "never received a value" meaning. Maxing at read time (instead of writing _to) is
        // what makes SetLastActivity safe against lock-free ingestion: neither writer can
        // lose the other's newer timestamp.
        internal override DateTime To
        {
            get
            {
                var to = _to;
                var floor = _lastActivity;

                if (to == DateTime.MaxValue)
                    return floor == DateTime.MinValue ? DateTime.MaxValue : floor;

                return to > floor ? to : floor;
            }
        }

        internal virtual T CalculateStatistics(T value) => value;

        internal virtual T RecalculateStatistics(T value) => value;


        internal virtual void AddValue(T value) => AddValueBase(value);

        internal virtual void AddValueBase(T value)
        {
            if (value.IsTimeout)
            {
                // Newest-wins marker update, and a hard stop: a marker that loses the
                // comparison is stale (a duplicate or out-of-order marker via TryAddValue) and
                // must never fall through to the value branch below, which would flip HasData
                // and install a timeout value as _lastValue.
                if (_lastTimeout is null || _lastTimeout.ReceivingTime < value.ReceivingTime)
                    _lastTimeout = value;

                return;
            }

            if (_lastValue is null || value.Time >= _lastValue.Time)
            {
                _lastValue = value;
                _to = value.Time;

                _cache.Enqueue(value);

                if (_cache.Count > CacheSize)
                    _cache.TryDequeue(out _);
            }
        }

        // Retry-only write (#1344): restores the activity floor ShouldDestroy()'s empty-cache
        // fallback keys on when the newest DB row is a real value, not a timeout marker.
        // Touches neither _lastValue nor _cache, and writes its own field rather than _to, so
        // the worst interleaving with lock-free ingestion is a redundant write of an older
        // timestamp into _lastActivity — To maxes over both and cannot regress.
        internal void SetLastActivity(DateTime time)
        {
            if (_lastActivity < time)
                _lastActivity = time;
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

        internal bool TryAggregateValue(T value)
        {
            if (IsLastEmptyOrTimeout || !LastValue.TryAggregateValue(value))
            {
                AddValue(value);
                return false;
            }

            return true;
        }

        internal bool IsNewSingletonValue(BaseValue value) => IsLastEmptyOrTimeout || LastValue.Time.Floor(_singletonTimePrecision) < value.Time.Floor(_singletonTimePrecision);


        internal override List<BaseValue> GetValues(int count) =>
            _cache.Take(count).Select(v => (BaseValue)v).ToList();

        internal override List<BaseValue> GetValues(DateTime from, DateTime to) =>
            _cache.Where(v => v.InRange(from, to)).Select(u => (BaseValue)u).ToList();

        internal override void Clear(DateTime to)
        {
            while (_cache.FirstOrDefault()?.LastUpdateTime <= to)
                _cache.TryDequeue(out _);

            if (_cache.IsEmpty)
                _lastValue = null;
        }


        internal override void Cut(DateTime time)
        {
            _from = time;
        }

        internal override void Clear()
        {
            _cache.Clear();

            _lastValue = null;
        }
    }
}