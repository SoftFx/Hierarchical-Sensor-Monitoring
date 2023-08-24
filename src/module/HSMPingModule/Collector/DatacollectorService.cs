namespace HSMPingModule.Collector;

internal sealed class DatacollectorService : BackgroundService
{
    private readonly DataCollectorWrapper _collector;


    public DatacollectorService(DataCollectorWrapper collector)
    {
        _collector = collector;
    }


    public override Task StopAsync(CancellationToken _) => _collector.Stop();


    protected override async Task ExecuteAsync(CancellationToken token) => await _collector.Start();
}