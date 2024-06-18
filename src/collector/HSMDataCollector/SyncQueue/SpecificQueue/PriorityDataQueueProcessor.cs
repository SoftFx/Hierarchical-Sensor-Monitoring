using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class PriorityDataQueueProcessor : EventedQueueProcessorBase<SensorValueBase>
    {
        public PriorityDataQueueProcessor(CollectorOptions options) : base(options) { }

        protected override async Task ProcessingLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await _event.WaitAsync(token);

                while (_queue.Count > 0)
                {
                    await _sender.SendPriorityDataAsync(_queue.Take(_options.MaxValuesInPackage), token).ConfigureAwait(false);
                }
            }
        }

    }
}
