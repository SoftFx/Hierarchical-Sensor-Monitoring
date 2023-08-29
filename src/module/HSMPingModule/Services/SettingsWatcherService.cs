using HSMPingModule.Config;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Services;

internal class SettingsWatcherService : BackgroundService
{
    private static bool _isFired = false;

    private readonly IOptionsMonitor<ServiceConfig> _config;
    private readonly FileSystemWatcher _watcher;


    public SettingsWatcherService(IOptionsMonitor<ServiceConfig> config)
    {
        _config = config;

        _watcher = new FileSystemWatcher(ServiceConfig.ConfigPath, ServiceConfig.ConfigName)
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

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