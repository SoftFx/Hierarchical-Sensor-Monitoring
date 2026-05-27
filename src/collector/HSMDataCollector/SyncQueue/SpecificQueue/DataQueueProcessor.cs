using System;
using System.Linq;
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
        public override string QueueName => "Data";

        public DataQueueProcessor(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger) : base(options, queueManager, logger) { }

        internal async Task FlushAsync(CancellationToken token)
        {
            try
            {
                while (QueueCount > 0 && !token.IsCancellationRequested)
                {
                    var package = GetPackage();

                    if (!package.Items.Any())
                        continue;

                    try
                    {
                        var sendingInfo = await _sender.SendDataAsync(package.Items, token).ConfigureAwait(false);
                        _queueManager.AddPackageSendingInfo(sendingInfo);
                        _queueManager.AddPackageInfo(QueueName, package.GetInfo());
                    }
                    catch (OperationCanceledException) { throw; }
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
            }
        }

        protected override async Task ProcessingLoop(CancellationToken token)
        {
            DataPackage<SensorValueBase> package;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.PackageCollectPeriod, token).ConfigureAwait(false);

                    if (IsEmpty)
                        continue;

                    do
                    {
                        package = GetPackage();

                        if (!package.Items.Any())
                            continue;

                        try
                        {
                            var sendingInfo = await _sender.SendDataAsync(package.Items, _cancellationTokenSource.Token).ConfigureAwait(false);
                            _queueManager.AddPackageSendingInfo(sendingInfo);
                            _queueManager.AddPackageInfo(QueueName, package.GetInfo());
                        }
                        catch (OperationCanceledException) { throw; }
                        catch
                        {
                            foreach (var item in package.Items)
                                Enqeue(item);

                            throw;
                        }
                    }
                    while (QueueCount >= _options.MaxValuesInPackage && !token.IsCancellationRequested);
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
