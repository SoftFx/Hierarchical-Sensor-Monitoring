using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class DataQueueProcessor : QueueProcessorBase<SensorValueBase>
    {
        protected override string QueueName => "Data";

        public DataQueueProcessor(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger) : base(options, queueManager, logger) { }

        protected override async Task ProcessingLoop(CancellationToken token)
        {
            DataPackage<SensorValueBase> package;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.PackageCollectPeriod, token).ConfigureAwait(false);

                    do
                    {
                        package = GetPackage();
                        var sendingInfo = await _sender.SendDataAsync(package.Items, _cancellationTokenSource.Token).ConfigureAwait(false);
                        _queueManager.AddPackageSendingInfo(sendingInfo);
                        _queueManager.AddPackageInfo(QueueName, package.GetInfo());
                    }
                    while (_queue.Count >= _options.MaxValuesInPackage && !token.IsCancellationRequested);
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
