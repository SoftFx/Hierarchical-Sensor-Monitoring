using HSMDataCollector.Core;
using HSMDataCollector.Requests;
using System;
using System.Threading.Tasks;

namespace HSMDataCollector.SyncQueue
{
    internal class CommandsQueue : SyncQueue<PriorityRequest>, ICommandQueue
    {
        public CommandsQueue(CollectorOptions options) : base(options, TimeSpan.FromSeconds(1)) { }


        public Task<bool> CallServer(PriorityRequest request)
        {
            throw new NotImplementedException();
        }
    }
}