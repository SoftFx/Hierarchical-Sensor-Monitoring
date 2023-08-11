using HSMDataCollector.Requests;
using System;
using System.Threading.Tasks;

namespace HSMDataCollector.SyncQueue
{
    public interface ICommandQueue : ISyncQueue<PriorityRequest>
    {
        Task<bool> CallServer(PriorityRequest request);


        void SetResult((Guid, string) key, bool result);

        void SetCancel((Guid, string) key);
    }
}