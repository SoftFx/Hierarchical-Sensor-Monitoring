using HSMPingModule;
using HSMPingModule.Config;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.SetBasePath(PingConfig.ConfigPath)
                     .AddJsonFile(PingConfig.ConfigName, true);

var config = new PingConfig(builder.Configuration);

var app = builder.Build();

app.Run();