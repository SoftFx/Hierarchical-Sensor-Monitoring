using System.Net.NetworkInformation;
using HSMPingModule.Config;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Services;

internal class PingService : BackgroundService
{
    private readonly PingConfig _config;
    private readonly List<string> _webSites;
    private readonly int _delay = 15000;

    public PingService(IOptions<PingConfig> config)
    {
        _config = config.Value;
        _webSites = _config.VpnSettings.WebSites;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var host in _webSites)
                Ping(host).ContinueWith((reply) => Print(reply.Result), stoppingToken);
        }
    }


    private async Task<PingReply> Ping(string host)
    {
        var myPing = new Ping();
        var buffer = new byte[32];
        var timeout = 1000;
        var pingOptions = new PingOptions();
        return await myPing.SendPingAsync(host, timeout, buffer, pingOptions);
    }

    private void Print(PingReply reply)
    {
        Console.WriteLine("-----------------------------");
        Console.WriteLine("-----------------------------");
        Console.WriteLine("Status: " + IPStatus.Success);
        Console.WriteLine("Address: " + reply.Address);
        Console.WriteLine("roundtrip time: " + reply.RoundtripTime);
        Console.WriteLine("-----------------------------");
        Console.WriteLine("-----------------------------");
    }
}
