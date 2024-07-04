using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class FileQueueProcessor: EventedQueueProcessorBase<FileSensorValue>
    {
        protected override string QueueName => "File";

        public FileQueueProcessor(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger) : base(options, queueManager, logger) { }

        protected override async Task ProcessingLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _event.Wait(token);
                    _event.Reset();

                    while (!_queue.IsEmpty && !token.IsCancellationRequested)
                    {
                        if (_queue.TryDequeue(out QueueItem<FileSensorValue> item))
                            await _sender.SendFileAsync(item.Value, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                }
            }
        }
    }
}
