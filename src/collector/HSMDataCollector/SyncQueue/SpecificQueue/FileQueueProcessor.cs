using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class FileQueueProcessor: QueueProcessorBase<FileSensorValue>
    {
        public override string QueueName => "File";

        public FileQueueProcessor(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger) : base(options, queueManager, logger) { }

        protected override async Task ProcessingLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await WaitToReadAsync(token).ConfigureAwait(false);

                    while (!IsEmpty && !token.IsCancellationRequested)
                    {
                        if (TryDequeue(out QueueItem<FileSensorValue> item))
                        {
                            try
                            {
                                var sendingInfo = await _sender.SendFileAsync(item.Value, token).ConfigureAwait(false);
                                EnsureSendSucceeded(sendingInfo, token);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch
                            {
                                Enqeue(item.Value);
                                throw;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                    await DelayAfterFailureAsync(token).ConfigureAwait(false);
                }
            }
        }
    }
}
