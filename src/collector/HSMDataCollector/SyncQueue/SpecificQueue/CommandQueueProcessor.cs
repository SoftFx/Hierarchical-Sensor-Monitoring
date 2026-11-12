using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMSensorDataObjects;


namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class CommandQueueProcessor : QueueProcessorBase<CommandRequestBase>
    {
        public override string QueueName => "Command";

        public CommandQueueProcessor(CollectorOptions options, DataProcessor queueManager, ICollectorLogger logger) : base(options, queueManager, logger) { }

        // A registration the server refused (bad payload, bad or blocked key) is refused again on
        // every repeat, so it is not re-enqueued. The native collector has the same rule (#1515).
        protected override bool RejectionIsFinal => true;

        protected override async ValueTask<bool> TryDispatchOneAsync(CancellationToken token)
        {
            var package = GetPackage();
            if (package.Count == 0)
                return false;

            await DispatchPackageAsync(package,
                                       (items, t) => _sender.SendCommandAsync(items, t),
                                       token).ConfigureAwait(false);
            return true;
        }
    }
}
