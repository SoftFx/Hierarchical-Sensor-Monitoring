using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseSettings;
using HSMServer.Core.DataLayer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HSMServer.Authentication
{
    // Default sink: a bounded channel drained by one background writer, storing through
    // IDatabaseCore into the append-only ApiTokenSecurityEvent table. Sampling is the
    // volume control for successes (1 of every SuccessSampleWindow); failures and
    // authorization denials are never sampled. Dispose drains the queue (bounded wait) so
    // a clean shutdown does not drop the pending window.
    public sealed class ApiTokenSecurityEventSink : IApiTokenSecurityEventSink, IDisposable
    {
        private const int QueueCapacity = 1024;

        // Every Nth successful authentication is recorded.
        private const int SuccessSampleWindow = 16;

        // How long Dispose waits for the pending window to drain.
        private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(3);


        private readonly IDatabaseCore _databaseCore;
        private readonly ILogger<ApiTokenSecurityEventSink> _logger;
        private readonly Channel<ApiTokenSecurityEventEntity> _queue =
            Channel.CreateBounded<ApiTokenSecurityEventEntity>(new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropNewest,
            });

        private readonly Task _writer;

        private long _dropped;
        private long _successCounter;


        public ApiTokenSecurityEventSink(IDatabaseCore databaseCore, ILogger<ApiTokenSecurityEventSink> logger)
        {
            _databaseCore = databaseCore ?? throw new ArgumentNullException(nameof(databaseCore));
            _logger = logger ?? NullLogger<ApiTokenSecurityEventSink>.Instance;

            _writer = Task.Run(WriteLoop);
        }


        public long DroppedCount => _dropped;


        public void Record(ApiTokenSecurityEvent @event)
        {
            if (@event is null)
                return;

            // Sampling: successes only. The counter is global — sampling must not become
            // per-token correlated volume an attacker can steer.
            if (@event.Kind == ApiTokenSecurityEventKind.AuthSucceeded &&
                Interlocked.Increment(ref _successCounter) % SuccessSampleWindow != 1)
                return;

            var entity = new ApiTokenSecurityEventEntity
            {
                Kind = (byte)@event.Kind,
                TokenId = @event.TokenId,
                OwnerUserId = @event.OwnerUserId,
                Operation = @event.Operation,
                TargetId = @event.TargetId,
                CorrelationId = @event.CorrelationId,
                Source = @event.Source,
                TimestampUtc = DateTime.UtcNow.Ticks,
            };

            if (!_queue.Writer.TryWrite(entity))
            {
                // Bounded queue: dropping is the documented loss bound under load. The
                // first drop and every 1024th after it are logged — visible, never noisy.
                var dropped = Interlocked.Increment(ref _dropped);

                if (dropped == 1 || dropped % QueueCapacity == 0)
                    _logger.LogWarning("API token security event dropped: queue at capacity ({Dropped} dropped so far)", dropped);
            }
        }

        public void Dispose()
        {
            _queue.Writer.TryComplete();

            try
            {
                _writer.Wait(DrainTimeout);
            }
            catch (AggregateException)
            {
                // The writer's own catch handles per-event failures; a crash here must not
                // take shutdown down.
            }
        }


        private async Task WriteLoop()
        {
            await foreach (var entity in _queue.Reader.ReadAllAsync())
            {
                try
                {
                    _databaseCore.PutApiTokenSecurityEvent(entity);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _dropped);

                    // Safe identifiers only on this path, per the entity's invariant.
                    _logger.LogWarning(ex, "Dropped an API token security event of kind {Kind}", entity.Kind);
                }
            }
        }
    }
}
