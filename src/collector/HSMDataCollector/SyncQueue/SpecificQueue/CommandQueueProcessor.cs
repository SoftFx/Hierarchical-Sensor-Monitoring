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

                        if (!package.Items.Any())
                            continue;

                        try
                        {
                            var sendingInfo =  await _sender.SendCommandAsync(package.Items, token).ConfigureAwait(false);
                            EnsureSendSucceeded(sendingInfo, token);
                            _queueManager.AddPackageSendingInfo(sendingInfo);
                            _queueManager.AddPackageInfo(QueueName, package.GetInfo());
                        }
                        catch (OperationCanceledException)
                        {
                            if (PreserveCanceledPackages)
                            {
                                foreach (var item in package.Items)
                                    Enqeue(item);
                            }

                            throw;
                        }
                        catch
                        {
                            foreach (var item in package.Items)
                                Enqeue(item);

                            throw;
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

        internal async Task FlushAsync(CancellationToken token)
        {
            while (QueueCount > 0 && !token.IsCancellationRequested)
            {
                var package = GetPackage();

                if (!package.Items.Any())
                    continue;

                try
                {
                    var sendingInfo = await _sender.SendCommandAsync(package.Items, token).ConfigureAwait(false);
                    EnsureSendSucceeded(sendingInfo, token);
                    _queueManager.AddPackageSendingInfo(sendingInfo);
                    _queueManager.AddPackageInfo(QueueName, package.GetInfo());
                }
                catch (OperationCanceledException)
                {
                    if (PreserveCanceledPackages)
                    {
                        foreach (var item in package.Items)
                            Enqeue(item);
                    }

                    throw;
                }
                catch (Exception ex)
                {
                    foreach (var item in package.Items)
                        Enqeue(item);

                    _logger.Error(ex);
                    throw;
                }
            }
        }

    }
}
