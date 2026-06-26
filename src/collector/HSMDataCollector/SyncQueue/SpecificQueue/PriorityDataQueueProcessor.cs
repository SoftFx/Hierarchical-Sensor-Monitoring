using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class PriorityDataQueueProcessor : QueueProcessorBase<SensorValueBase>
    {
        public override string QueueName => "Priority data";

        public PriorityDataQueueProcessor(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger) : base(options, queueManager, logger) { }

        protected override async ValueTask<bool> TryDispatchOneAsync(CancellationToken token)
        {
            var package = GetPackage();
            if (package.Count == 0)
                return false;

            await DispatchPackageAsync(package,
                                       (items, t) => _sender.SendPriorityDataAsync(items, t),
                                       token).ConfigureAwait(false);
            return true;
        }
    }
}
