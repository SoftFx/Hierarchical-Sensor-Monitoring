using HSMServer.Core.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class ValuesStorage
    {
        protected virtual int CacheSize => 100;


        internal abstract BaseValue LastValue { get; }

        internal abstract bool HasData { get; }


        internal virtual BaseValue LastDbValue => LastValue;


        internal abstract List<BaseValue> GetValues(DateTime from, DateTime to);

        internal abstract List<BaseValue> GetValues(int count);

        internal abstract void Clear(DateTime to);

        internal abstract void Clear();
    }


    public abstract class ValuesStorage<T> : ValuesStorage where T : BaseValue
    {
        private readonly ConcurrentQueue<T> _cache = new();


        internal override bool HasData => !_cache.IsEmpty;

        internal override T LastValue => _cache.LastOrDefault();


        internal virtual void AddValue(T value) => AddValueBase(value);

        internal virtual void AddValueBase(T value)
        {
            _cache.Enqueue(value);

            if (_cache.Count > CacheSize)
                _cache.TryDequeue(out _);
        }


        internal override List<BaseValue> GetValues(int count) =>
            _cache.Take(count).Select(v => (BaseValue)v).ToList();

        internal override List<BaseValue> GetValues(DateTime from, DateTime to) =>
            _cache.Where(v => v.InRange(from, to)).Select(u => (BaseValue)u).ToList();

        internal override void Clear(DateTime to)
        {
            while (_cache.FirstOrDefault()?.ReceivingTime <= to)
                _cache.TryDequeue(out _);
        }

        internal override void Clear() => _cache.Clear();
    }
}
