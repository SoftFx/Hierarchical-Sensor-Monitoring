using HSMServer.Core.Cache;
using System;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public sealed class MonitoringBackgroundService : BaseDelayedBackgroundService
    {
        private readonly ITreeValuesCache _cache;


        public override TimeSpan Delay { get; } = new TimeSpan(0, 1, 1); // 1 extra second to apply all updates


        public MonitoringBackgroundService(ITreeValuesCache cache)
        {
            _cache = cache;
        }


        protected override Task ServiceActionAsync()
        {
           // _cache.UpdateCacheState();

            return Task.CompletedTask;
        }
    }
}