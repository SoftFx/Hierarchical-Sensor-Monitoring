using HSMDataCollector.Requests;
using System.Threading.Tasks;

namespace HSMDataCollector.SyncQueue
{
    public interface ICommandQueue : ISyncQueue<PriorityRequest>
    {
        Task<bool> CallServer(PriorityRequest request);
    }
}