using HSMDataCollector.Core;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.SyncQueue
{
    public interface ISyncQueue<T>
    {
        event Action<List<T>> NewValuesEvent;
        event Action<T> NewValueEvent;

        void Push(T value);

        void PushFailValue(T value);
    }
}