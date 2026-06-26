using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class DataQueueProcessor : QueueProcessorBase<SensorValueBase>
    {
        // Floor mirrors DelayAfterFailureAsync so a misconfigured PackageCollectPeriod of zero
        // cannot busy-spin the processing loop.
        private static readonly TimeSpan MinCollectPeriod = TimeSpan.FromMilliseconds(100);

        public override string QueueName => "Data";

        public DataQueueProcessor(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger) : base(options, queueManager, logger) { }

        protected override async ValueTask WaitForReadyAsync(CancellationToken token)
        {
            var period = _options.PackageCollectPeriod > TimeSpan.Zero
                ? _options.PackageCollectPeriod
                : MinCollectPeriod;

            await Task.Delay(period, token).ConfigureAwait(false);
        }

        protected override async ValueTask<bool> TryDispatchOneAsync(CancellationToken token)
        {
            var package = GetPackage();
            if (package.Count == 0)
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
