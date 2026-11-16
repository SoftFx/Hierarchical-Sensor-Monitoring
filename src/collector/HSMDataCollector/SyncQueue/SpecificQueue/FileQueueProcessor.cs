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

            // A file the sender could never send (larger than a collector session frame) is lost
            // for good, and counted where every lost value is counted.
            if (sendingInfo.DroppedCount > 0)
                _queueManager?.ReportRequeueEviction(QueueName, sendingInfo.DroppedCount);

            if (sendingInfo.Error != null)
            {
                var retryResult = ReEnqueueItem(item);
                var preserved = retryResult.IsAccepted ? 1 - retryResult.DroppedCount : 0;
                // A file that only lacked a connection is not an error of the queue: the queue waits
                // for the transport and the transport reports its own state, as for the other queues.
                throw new PackageSendFailedException(QueueName, preserved, retryResult.DroppedCount, IsFlushing, sendingInfo.Error, sendingInfo.IsTransportDown);
            }

            return true;
        }
    }
}
