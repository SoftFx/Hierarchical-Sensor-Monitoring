using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class FileQueueProcessor: EventedQueueProcessorBase<FileSensorValue>
    {
        public FileQueueProcessor(CollectorOptions options) : base(options) { }

        protected override async Task ProcessingLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await _event.WaitAsync(_options.PackageCollectPeriod, token);

                while (_queue.Count > 0)
                {
                    if (_queue.TryDequeue(out FileSensorValue result))
                        await _sender.SendFileAsync(result, token).ConfigureAwait(false);
                }
            }
        }
    }
}
