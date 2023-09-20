using HSMPingModule.Config;
using HSMPingModule.DataCollectorWrapper;
using HSMPingModule.Services;
using HSMPingModule.VpnManager;
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

    var config = new ServiceConfig(builder.Configuration, logger);

    builder.Services.AddSingleton(config)
                    .AddSingleton(VpnFactory.GetVpn(config.VpnSettings))
                    .AddSingleton<IDataCollectorWrapper, DataCollectorWrapper>();

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
