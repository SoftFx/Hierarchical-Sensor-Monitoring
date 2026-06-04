using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class FileQueueProcessor : QueueProcessorBase<FileSensorValue>
    {
        public override string QueueName => "File";

        public FileQueueProcessor(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger) : base(options, queueManager, logger) { }


        protected override async ValueTask<bool> TryDispatchOneAsync(CancellationToken token)
        {
            if (!TryDequeue(out QueueItem<FileSensorValue> item))
                return false;

            try
            {
                await _sender.SendFileAsync(item.Value, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (PreserveCanceledPackages)
                    ReEnqueueItem(item.Value);
                throw;
            }
            catch
            {
                ReEnqueueItem(item.Value);
                throw;
            }

            return true;
        }
    }
}
