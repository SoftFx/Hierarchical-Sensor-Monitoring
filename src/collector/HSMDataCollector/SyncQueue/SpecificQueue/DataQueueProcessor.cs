using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class DataQueueProcessor : QueueProcessorBase<SensorValueBase>
    {
        public override string QueueName => "Data";

        public DataQueueProcessor(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger) : base(options, queueManager, logger) { }


        protected override async ValueTask WaitForReadyAsync(CancellationToken token)
        {
            await Task.Delay(_options.PackageCollectPeriod, token).ConfigureAwait(false);
        }

        protected override async ValueTask<bool> TryDispatchOneAsync(CancellationToken token)
        {
            var package = GetPackage();
            if (!package.Items.Any())
                return false;

            await DispatchPackageAsync(package,
                                       (items, t) => _sender.SendDataAsync(items, t),
                                       token).ConfigureAwait(false);
            return true;
        }

        // Data queue batches by MaxValuesInPackage; keep draining only while another full batch is available.
        protected override bool ShouldContinueDispatching() => QueueCount >= _options.MaxValuesInPackage;
    }
}
