using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMDataCollector.SyncQueue
{
    public interface ISyncQueue<T>
    {
        event Func<List<T>, Task> NewValuesEvent;
        event Func<T, Task> NewValueEvent;

        event Action<string, int> OverflowCntEvent;


        void AddFail(T value);

        void Send(T value); //skip queue, send to server in separate request

        void Add(T value); //value to queue, send to server in package


        void ThrowPackageRequestInfo(PackageSendingInfo info);
    }
}