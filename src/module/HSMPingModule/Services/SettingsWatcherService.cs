using HSMPingModule.Config;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Services;

internal class SettingsWatcherService : BackgroundService
{
    private static bool _isFired = false;


    private readonly IOptionsMonitor<PingConfig> _config;
    private readonly FileSystemWatcher _watcher;


    public SettingsWatcherService(IOptionsMonitor<PingConfig> config)
    {
        _config = config;

        _watcher = new FileSystemWatcher(PingConfig.ConfigPath, PingConfig.ConfigName)
        {
            NotifyFilter = NotifyFilters.LastWrite
        };
        
        _watcher.EnableRaisingEvents = true;

        _watcher.Changed += OnAppSettingsChanged;
    }

    private void OnAppSettingsChanged(object sender, FileSystemEventArgs e)
    {
        if (!_isFired)
            _config.CurrentValue.Reload();

        _isFired = !_isFired;
    }
    

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}