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

            // As in DispatchPackageAsync: a send the stop drain has given up settles nothing (#1748).
            var drainSend = BeginDrainSend(items: 1);
            PackageSendingInfo sendingInfo;

            try
            {
                sendingInfo = await Sender.SendFileAsync(item.Value, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (OwnsPackage(drainSend, items: 1) && PreserveCanceledPackages)
                    ReEnqueueItem(item);
                throw;
            }
            catch
            {
                if (OwnsPackage(drainSend, items: 1))
                    ReEnqueueItem(item);
                throw;
            }

            if (!OwnsPackage(drainSend, items: 1))
                return true;

            if (sendingInfo.Error != null)
            {
                var retryResult = ReEnqueueItem(item);
                var preserved = retryResult.IsAccepted ? 1 - retryResult.DroppedCount : 0;
                throw new PackageSendFailedException(QueueName, preserved, retryResult.DroppedCount, IsFlushing, sendingInfo.Error);
            }

            return true;
        }
    }
}
