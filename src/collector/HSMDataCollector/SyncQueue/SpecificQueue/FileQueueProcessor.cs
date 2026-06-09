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

            PackageSendingInfo sendingInfo;

            try
            {
                sendingInfo = await _sender.SendFileAsync(item.Value, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (PreserveCanceledPackages)
                    ReEnqueueItem(item);
                throw;
            }
            catch
            {
                ReEnqueueItem(item);
                throw;
            }

            if (sendingInfo.Error != null)
            {
                ReEnqueueItem(item);
                throw new InvalidOperationException($"Failed to send package for {QueueName} (1 value preserved). {sendingInfo.Error}");
            }

            return true;
        }
    }
}
