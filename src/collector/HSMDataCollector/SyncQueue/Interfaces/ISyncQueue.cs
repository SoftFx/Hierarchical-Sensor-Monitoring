using System;
using System.Collections.Generic;

namespace HSMDataCollector.SyncQueue
{
    public interface ISyncQueue<T>
    {
        event Action<List<T>> NewValuesEvent;
        event Action<T> NewValueEvent;

        event Action<string, int> OverflowCntEvent;


        void AddFail(T value);

        void Send(T value); //skip queue, send to server in separate request

        void Add(T value); //value to queue, send to server in package
    }
}