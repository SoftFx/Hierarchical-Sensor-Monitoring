using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public class DatacollectorService : BaseDelayedBackgroundService
    {
        private readonly DataCollectorWrapper _collector;


        public override TimeSpan Delay { get; } = TimeSpan.FromMinutes(5);


        public DatacollectorService(DataCollectorWrapper collector)
        {
            _collector = collector;
        }


        public override Task StopAsync(CancellationToken token) =>
            RunAction(_collector.Stop, "Stop self collector").ContinueWith(_ => base.StopAsync(token)).Unwrap();


        protected override async Task ExecuteAsync(CancellationToken token)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            await RunAction(_collector.Start, "Start self collector");
            await base.ExecuteAsync(token);
        }

        protected override Task ServiceActionAsync()
        {
            _collector.SendDbInfo();

            return Task.CompletedTask;
        }
    }
}
