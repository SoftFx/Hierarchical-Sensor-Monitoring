using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class CommandQueueProcessor : QueueProcessorBase<CommandRequestBase>
    {
        public override string QueueName => "Command"; 

        public CommandQueueProcessor(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger) : base(options, queueManager, logger) { }

        protected override async Task ProcessingLoop(CancellationToken token)
        {
            DataPackage<CommandRequestBase> package;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await WaitToReadAsync(token).ConfigureAwait(false);

                    while (!IsEmpty && !token.IsCancellationRequested)
                    {
                        package = GetPackage();
                        var sendingInfo =  await _sender.SendCommandAsync(package, token).ConfigureAwait(false);
                        _queueManager.AddPackageSendingInfo(sendingInfo);
                        _queueManager.AddPackageInfo(QueueName, package.GetInfo());
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                }
            }
        }

    }
}
