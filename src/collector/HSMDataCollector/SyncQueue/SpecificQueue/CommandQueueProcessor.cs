using System.Collections.Generic;
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

        // A registration the server refused (bad payload, bad or blocked key) is refused again on
        // every repeat, so it is not re-enqueued. The native collector has the same rule (#1515).
        protected override bool RejectionIsFinal => true;

        // A lone registration is named by its path, a batch by its size, as the native collector
        // names a failed registration post.
        protected override string DescribeItems(DataPackage<CommandRequestBase> package)
        {
            if (package.Count == 1)
                foreach (var command in package)
                    return $"the command for {command.Path}";

            return $"{package.Count} command(s)";
        }

        protected override async ValueTask<bool> TryDispatchOneAsync(CancellationToken token)
        {
            var package = GetPackage();
            if (package.Count == 0)
                return false;

            await DispatchPackageAsync(package,
                                       (items, t) => _sender.SendCommandAsync(InOrderOfIssue(package), t),
                                       token).ConfigureAwait(false);
            return true;
        }

        // A package that failed returns to the tail of the queue, behind what was issued during
        // its send, and the server applies the commands of a package in the order they come.
        // Sent in queue order, an older registration of a sensor would then overwrite a newer
        // one. The order of issue is the order that is right for the server. This holds inside
        // a package only: with more commands queued than one package takes, a failed package
        // still rotates behind the packages issued after it (#1750). A list, not a
        // deferred query: a sender reads the commands more than once, and every reading must
        // see the same order without sorting again.
        private static IEnumerable<CommandRequestBase> InOrderOfIssue(DataPackage<CommandRequestBase> package) =>
            package.Count < 2 ? (IEnumerable<CommandRequestBase>)package : package.Items.OrderBy(item => item.BuildDate).Select(item => item.Value).ToList();
    }
}
