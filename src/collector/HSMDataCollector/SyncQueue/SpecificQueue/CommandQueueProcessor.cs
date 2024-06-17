using HSMDataCollector.Core;
using HSMSensorDataObjects;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.SyncQueue.SpecificQueue
{
    internal sealed class CommandQueueProcessor : EventedQueueProcessorBase<CommandRequestBase>
    {
        public CommandQueueProcessor(CollectorOptions options) : base(options) { }

        protected override async Task ProcessingLoop(CancellationToken token)
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                await _event.WaitAsync(token);

                await _sender.SendCommandAsync(_queue.Take(_options.MaxQueueSize), token).ConfigureAwait(false);
            }
        }

    }
}
