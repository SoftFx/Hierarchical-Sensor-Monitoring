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

        // An older command must never reach the server after a newer one (#1750): the server
        // keeps what came last, so an old registration of a sensor would overwrite a new one.
        // A failed package therefore does not return to the tail of the queue, behind the
        // commands issued after it; its commands are held and the next package starts with them.
        // The native collector needs no such rule: it queues the sensor, not the command, and
        // posts the sensor's state as it is at the time of the post.
        protected override bool KeepsOrderOfIssue => true;

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

            // The server applies the commands of a package in the order they come, so they are
            // sent in the order they were issued in: an older registration of a sensor must not
            // follow, and overwrite, a newer one. Across packages KeepsOrderOfIssue sees to it
            // (the package starts with what an earlier one could not deliver). Inside the
            // package this sort does: two threads that issue commands at the same time can
            // write them to the queue in the other order than they took their numbers in, and
            // such a command can arrive behind held ones that were issued after it.
            //
            // The package itself is sorted, not a copy of it: a sender that delivers a package in
            // parts answers with the positions it delivered, and those are read against the
            // package. With a sorted copy the positions named other commands than the ones sent:
            // a delivered registration went out again and one that never arrived was dropped.
            package.SortByIssue();

            var sender = Sender;

            await DispatchPackageAsync(package,
                                       (items, t) => sender.SendCommandAsync(items, t),
                                       token).ConfigureAwait(false);
            return true;
        }
    }
}
