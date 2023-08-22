using HSMPingModule.Config;
using DatacollectorService = HSMPingModule.Collector.DatacollectorService;
using DataCollectorWrapper = HSMPingModule.Collector.DataCollectorWrapper;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.SetBasePath(PingConfig.ConfigPath)
                     .AddJsonFile(PingConfig.ConfigName, true);

var config = new PingConfig(builder.Configuration);
DataCollectorWrapper.SetConfig(config);

builder.Services.AddSingleton<DataCollectorWrapper>();;
builder.Services.AddHostedService<DatacollectorService>();
                

var app = builder.Build();

app.Run();