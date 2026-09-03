using System;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Authentication;

namespace HSMServer.BackgroundServices.DatabaseServices
{
    // Hourly driver for ApiTokenRetentionCleaner. The first pass runs at the next whole-
    // hour boundary after boot + StartDelay (BaseDelayedBackgroundService computes the
    // initial delay as (now + StartDelay).Ceil(Delay) - now, which rounds UP to a Delay
    // multiple — with Delay = 1h, StartDelay only decides which boundary when boot falls
    // within it), so boot (index load, certificate setup, collector reconnects) settles
    // before the first sweep. Each pass is bounded, so a large backlog drains over
    // consecutive passes rather than in one long-running sweep.
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
