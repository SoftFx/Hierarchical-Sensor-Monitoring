using System;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.Cache;


namespace HSMServer.BackgroundServices
{
    public class ClearDatabaseService : BaseDelayedBackgroundService
    {
        private readonly ITreeValuesCache _cache;


        public override TimeSpan Delay { get; } = TimeSpan.FromHours(1);


        public ClearDatabaseService(ITreeValuesCache cache)
        {
            _cache = cache;
        }

        //uncomment for immediately running
        //protected override Task ExecuteAsync(CancellationToken token)
        //{
        //    ServiceAction();
        //    return base.ExecuteAsync(token);
        //}

        protected override async Task ServiceActionAsync(CancellationToken token)
        {
            await _cache.CheckSensorsHistoryAsync(token);
            await _cache.RunSensorsSelfDestroyAsync(token);
            await _cache.RunProductsSelfDestroyAsync(token);
        }

    }
}
