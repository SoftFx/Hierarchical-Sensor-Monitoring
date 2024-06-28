using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class CommandQueueProcessor : EventedQueueProcessorBase<CommandRequestBase>
    {
        protected override string QueueName => "Command"; 

        public CommandQueueProcessor(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger) : base(options, queueManager, logger) { }

        protected override async Task ProcessingLoop(CancellationToken token)
        {
            DataPackage<CommandRequestBase> package;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _event.Wait(token);
                    _event.Reset();

                    while (!_queue.IsEmpty && !token.IsCancellationRequested)
                    {
                        package = GetPackage();
                        var sendingInfo =  await _sender.SendCommandAsync(package.Items.ToList(), token).ConfigureAwait(false);
                        _queueManager.AddPackageSendingInfo(sendingInfo);
                        _queueManager.AddPackageInfo(QueueName, package.GetInfo());
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
