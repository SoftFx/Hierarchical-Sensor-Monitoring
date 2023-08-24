using HSMPingModule.Collector;
using HSMPingModule.Config;
using HSMPingModule.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.SetBasePath(ServiceConfig.ConfigPath)
                     .AddJsonFile(ServiceConfig.ConfigName, true);

builder.Services.Configure<ServiceConfig>(config => config.SetUpConfig(builder.Configuration));

builder.Services.AddSingleton<DataCollectorWrapper>();

builder.Services.AddHostedService<SettingsWatcherService>();
builder.Services.AddHostedService<DatacollectorService>();
builder.Services.AddHostedService<PingService>();

var app = builder.Build();

app.Run();