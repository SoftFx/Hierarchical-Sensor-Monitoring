using System;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Authentication;

namespace HSMServer.BackgroundServices.DatabaseServices
{
    // Hourly driver for ApiTokenRetentionCleaner. Delayed start so boot (index load,
    // certificate setup, collector reconnects) settles before the first sweep; each pass
    // is bounded, so a large backlog drains over consecutive passes rather than in one
    // long-running sweep.
    public class ApiTokenRetentionService : BaseDelayedBackgroundService
    {
        public override TimeSpan StartDelay { get; } = TimeSpan.FromMinutes(5);

        public override TimeSpan Delay { get; } = TimeSpan.FromHours(1);


        private readonly ApiTokenRetentionCleaner _cleaner;

        public ApiTokenRetentionService(ApiTokenRetentionCleaner cleaner)
        {
            _cleaner = cleaner;
        }

        protected override Task ServiceActionAsync(CancellationToken token = default)
        {
            _cleaner.RunOnce(DateTime.UtcNow);
            return Task.CompletedTask;
        }
    }
}
