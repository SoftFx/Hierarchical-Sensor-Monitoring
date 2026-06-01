using System.Linq;
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


        protected override async ValueTask<bool> TryDispatchOneAsync(CancellationToken token)
        {
            var package = GetPackage();
            if (!package.Items.Any())
                return false;

            await DispatchPackageAsync(package,
                                       (items, t) => _sender.SendCommandAsync(items, t),
                                       token).ConfigureAwait(false);
            return true;
        }
    }
}
