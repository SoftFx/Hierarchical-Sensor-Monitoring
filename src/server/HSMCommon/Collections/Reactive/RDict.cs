using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMCommon.Collections.Reactive
{
    public sealed class RDict<T> : RDictBase<Guid, T>
    {
        public RDict(Dictionary<Guid, T> dict, Action reaction) : base(dict, reaction) { }

        public RDict(Action reaction) : base(reaction) { }
    }


    public readonly struct RDictResult<T>
    {
        private readonly Action _reaction;


        public static RDictResult<T> ErrorResult { get; } = new(false, default);

        public bool IsOk { get; }

        public T Value { get; }


        private RDictResult(bool ok, T value)
        {
            IsOk = ok;
            Value = value;
        }

        public RDictResult(bool ok, T value, Action reaction) : this(ok, value)
        {
            _reaction = reaction;
        }


        public readonly RDictResult<T> ThenCallForSuccess(Action<T> customReaction)
        {
            if (IsOk)
                customReaction?.Invoke(Value);

            return this;
        }

        public readonly RDictResult<T> ThenCall()
        {
            if (IsOk)
                _reaction?.Invoke();

            return this;
        }
    }


    public abstract class RDictBase<TKey, TValue> : ConcurrentDictionary<TKey, TValue>
    {
        private readonly Action _reaction;


        public RDictBase(Dictionary<TKey, TValue> dict, Action reaction) : base(dict)
        {
            _reaction = reaction;
        }

        protected RDictBase(Action reaction) : base()
        {
            _reaction = reaction;
        }


        public RDictResult<TValue> IfTryAdd(TKey key, TValue value, Action<TValue> successReaction = null)
        {
            var result = ToResult(TryAdd(key, value), value);

            if (result.IsOk && successReaction is not null)
                successReaction?.Invoke(value);

            return result;
        }

        public bool TryCallAdd(TKey key, TValue value, Action<TValue> successReaction = null) => IfTryAdd(key, value, successReaction).ThenCall().IsOk;


        public RDictResult<TValue> IfTryRemove(TKey key)
        {
            var result = TryRemove(key, out var value);

            return ToResult(result, value);
        }

        public RDictResult<TValue> IfTryRemoveAndDispose(TKey key)
        {
            var result = TryRemove(key, out var value);

            if (result && value is IDisposable dispose)
                dispose.Dispose();

            return ToResult(result, value);
        }

        public bool TryCallRemoveAndDispose(TKey key) => IfTryRemoveAndDispose(key).ThenCall().IsOk;

        public bool TryCallRemove(TKey key) => IfTryRemove(key).ThenCall().IsOk;


        private RDictResult<TValue> ToResult(bool result, TValue value) => new(result, value, _reaction);
    }
}