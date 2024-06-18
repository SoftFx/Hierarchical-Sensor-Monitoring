using HSMDataCollector.Core;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal abstract class EventedQueueProcessorBase<T> : QueueProcessorBase<T>
    {
        protected readonly SemaphoreSlim _event = new SemaphoreSlim(0);

        public EventedQueueProcessorBase(CollectorOptions options) : base (options) { }

        public override int Enqeue(T item)
        {
            int result = base.Enqeue(item);
            _event.Release();
            return result;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _event?.Dispose();
            }
        }

    }
}
