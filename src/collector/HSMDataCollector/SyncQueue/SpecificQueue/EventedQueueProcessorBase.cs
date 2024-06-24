using System.Threading;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMSensorDataObjects;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal abstract class EventedQueueProcessorBase<T> : QueueProcessorBase<T>
    {
        protected readonly AutoResetEvent _event = new AutoResetEvent(false);

        public EventedQueueProcessorBase(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger) : base (options, queueManager, logger) { }

        internal override int Enqeue(T item)
        {
            int result = base.Enqeue(item);
            _event.Set();
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
