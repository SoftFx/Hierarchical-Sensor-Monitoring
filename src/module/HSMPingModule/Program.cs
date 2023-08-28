using HSMPingModule.Collector;
using HSMPingModule.Config;
using HSMPingModule.Services;
using HSMPingModule.Services.Interfaces;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.SetBasePath(ServiceConfig.ConfigPath)
                     .AddJsonFile(ServiceConfig.ConfigName, true);

builder.Services.Configure<ServiceConfig>(config => config.SetUpConfig(builder.Configuration));

builder.Services.AddSingleton<IDataCollectorService, DataCollectorService>();

builder.Services.AddHostedService<SettingsWatcherService>();
builder.Services.AddHostedService<DataCollectorService>();
builder.Services.AddHostedService<PingService>();

var app = builder.Build();

app.Run();