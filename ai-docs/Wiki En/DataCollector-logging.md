### Default
[NLog](https://nlog-project.org/) is used by default. Default settings are:
* WriteDebug = false
* ConfigPath = "collector.nlog.config"
```C#
var collectorOptions = new CollectorOptions()
{
    AccessKey = "e6150991-08a8-48dc-8152-0458715a1e3c", //should be changed
    ServerAddress = "https://localhost",
};

var collector = new DataCollector(collectorOptions).AddNLog();
```

### Custom options
You can change the default NLog settings the next way:
```C#
var collectorOptions = new CollectorOptions()
{
    AccessKey = "e6150991-08a8-48dc-8152-0458715a1e3c", //should be changed
    ServerAddress = "https://localhost",
};
var loggerOptions = new LoggerOptions()
{
    ConfigPath = "logger.config";
    WriteDebug = true,
};

var collector = new DataCollector(collectorOptions).AddNLog(loggerOptions);
```

### Custom logger
You can use your custom logger. It should implement interface ICollectorLogger.
```C#
internal sealed class CustomLogger : ICollectorLogger
{
    public void Debug(string message) => Console.WriteLine($"Debug: {message}");

    public void Info(string message) => Console.WriteLine($"Info: {message}");

    public void Error(string message) => Console.WriteLine($"Error: {message}");

    public void Error(Exception ex) => Console.WriteLine($"Exception: {ex.Message}");
}


var collectorOptions = new CollectorOptions()
{
    AccessKey = "e6150991-08a8-48dc-8152-0458715a1e3c", //should be changed
    ServerAddress = "https://localhost",
};

var collector = new DataCollector(collectorOptions).AddCustomLogger(new CustomLogger());
```