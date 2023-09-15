using HSMPingModule.Collector;
using HSMPingModule.Config;
using HSMPingModule.Services;
using HSMPingModule.Services.Interfaces;
using NLog;
using NLog.Extensions.Logging;
using NLog.Web;

var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Logging.ClearProviders();
    builder.Logging.AddNLog();

    builder.Configuration.SetBasePath(ServiceConfig.ConfigPath)
                         .AddJsonFile(ServiceConfig.ConfigName, true);

    builder.Services.Configure<ServiceConfig>(config => config.SetUpConfig(builder.Configuration, logger));

    builder.Services.AddSingleton<IDataCollectorService, DataCollectorService>();

    builder.Services.AddHostedService<SettingsWatcherService>();
    builder.Services.AddHostedService<PingService>();

    var app = builder.Build();

    app.Run();
}
catch (Exception exception)
{
    logger.Fatal(exception, "Stopped program because of exception");
    throw;
}
finally
{
    LogManager.Shutdown();
}
