using DataCollectorSandboxCore;
using HSMDataCollector.Core;
using System.Text.Json;
using System.Text.Json.Serialization;



var dataSender = new DataSender();

var collectorOptions = new CollectorOptions()
{
    //ServerAddress = "hsm.dev.soft-fx.eu",
    AccessKey = "52e9b823-b50b-4c06-8640-ed79172a9fc1", //local key
    Module = "DataCollector Sandbox Core",
    ComputerName = "LocalMachine(Core)",
    DataSender = dataSender
};

var _collector = new DataCollector(collectorOptions).AddNLog(new HSMDataCollector.Logging.LoggerOptions() { WriteDebug = true });

//_collector.Windows.AddAllDefaultSensors();

var _sensor = _collector.CreateVersionSensor("Version");


await _collector.Start();

CancellationTokenSource tsc = new CancellationTokenSource();

Task.Run(() =>
{
    int i = 0;
    while (i < 100)
    {
        if (tsc.Token.IsCancellationRequested)
            break;

        _sensor.AddValue(new Version(i, i, i), $"Comment {i}");
        i++;
    }
});
Console.ReadLine();
tsc.Cancel();
tsc.Dispose();


_collector.Dispose();