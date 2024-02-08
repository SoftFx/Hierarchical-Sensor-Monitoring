using HSMServer.Core.Cache;
using System;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public sealed class MonitoringBackgroundService : BaseDelayedBackgroundService
    {
        private readonly ITreeValuesCache _cache;
        private readonly ClientStatistics _statistics;

        public override TimeSpan Delay { get; } = new TimeSpan(0, 1, 1); // 1 extra second to apply all updates


        public MonitoringBackgroundService(ITreeValuesCache cache, ClientStatistics statistics)
        {
            _cache = cache;
            _statistics = statistics;
        }


        protected override Task ServiceAction()
        {
            _cache.UpdateCacheState();
            
            return Task.CompletedTask;
        }
    }
}
