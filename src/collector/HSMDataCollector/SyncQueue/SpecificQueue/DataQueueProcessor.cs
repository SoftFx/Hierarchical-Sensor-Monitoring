using HSMDataCollector.Core;
using HSMSensorDataObjects.SensorValueRequests;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class DataQueueProcessor : QueueProcessorBase<SensorValueBase>
    {
        public DataQueueProcessor(CollectorOptions options) : base(options) { }

        protected override async Task ProcessingLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(_options.PackageCollectPeriod, token).ConfigureAwait(false);

                while (_queue.Count > 0)
                {
                    await _sender.SendDataAsync(_queue.Take(_options.MaxValuesInPackage), _cancellationTokenSource.Token).ConfigureAwait(false);
                }
            }
        }

    }
}
