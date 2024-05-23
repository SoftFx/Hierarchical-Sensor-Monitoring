using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public class DatacollectorService : BaseDelayedBackgroundService
    {
        private readonly DataCollectorWrapper _collector;

        private bool _isMonitoringEnabled;


        public override TimeSpan Delay { get; } = TimeSpan.FromMinutes(5);


        public DatacollectorService(DataCollectorWrapper collector, IServerConfig config, IOptionsMonitor<MonitoringOptions> optionsMonitor)
        {
            _collector = collector;

            _isMonitoringEnabled = config.MonitoringOptions.IsMonitoringEnabled;
            optionsMonitor.OnChange(MonitoringOptionsListener);
        }


        public override Task StopAsync(CancellationToken token) =>
            RunAction(_collector.Stop, "Stop self collector").ContinueWith(_ => base.StopAsync(token)).Unwrap();


        protected override async Task ExecuteAsync(CancellationToken token)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);

            if (_isMonitoringEnabled)
                await RunAction(_collector.Start, "Start self collector");

            await base.ExecuteAsync(token);
        }

        protected override Task ServiceAction()
        {
            if (_isMonitoringEnabled)
                _collector.SendDbInfo();

            return Task.CompletedTask;
        }

        private void MonitoringOptionsListener(MonitoringOptions options, string __)
        {
            if (options.IsMonitoringEnabled != _isMonitoringEnabled)
            {
                _isMonitoringEnabled = options.IsMonitoringEnabled;

                if (_isMonitoringEnabled)
                    _ = RunAction(_collector.Start, "Start self collector");
                else
                    _ = RunAction(_collector.Stop, "Stop self collector");
            }
        }
    }
}
