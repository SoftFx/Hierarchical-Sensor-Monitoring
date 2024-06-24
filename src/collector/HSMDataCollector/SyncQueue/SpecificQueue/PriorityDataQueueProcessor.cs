using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class PriorityDataQueueProcessor : EventedQueueProcessorBase<SensorValueBase>
    {
        protected override string QueueName => "Priority data";

        public PriorityDataQueueProcessor(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger) : base(options, queueManager, logger) { }

        protected override async Task ProcessingLoop(CancellationToken token)
        {
            DataPackage<SensorValueBase> package;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _event.WaitOne();

                    while (!_queue.IsEmpty && !token.IsCancellationRequested)
                    {
                        package = GetPackage();
                        var sendingInfo = await _sender.SendPriorityDataAsync(package.Items, token).ConfigureAwait(false);
                        _queueManager.AddPackageSendingInfo(sendingInfo);
                        _queueManager.AddPackageInfo(QueueName, package.GetInfo());
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                }
            }
        }

    }
}
