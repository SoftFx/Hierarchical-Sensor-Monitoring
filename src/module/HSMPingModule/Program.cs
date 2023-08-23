using HSMPingModule.Config;
using DatacollectorService = HSMPingModule.Collector.DatacollectorService;
using DataCollectorWrapper = HSMPingModule.Collector.DataCollectorWrapper;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.SetBasePath(PingConfig.ConfigPath)
                     .AddJsonFile(PingConfig.ConfigName, true);

builder.Services.Configure<PingConfig>(config => config.SetUpConfig(builder.Configuration));
builder.Services.AddSingleton<DataCollectorWrapper>();;
builder.Services.AddHostedService<DatacollectorService>();

var app = builder.Build();

app.Run();