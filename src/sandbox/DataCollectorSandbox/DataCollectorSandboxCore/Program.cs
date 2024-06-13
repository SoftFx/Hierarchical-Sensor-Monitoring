using HSMDataCollector.Core;

var collectorOptions = new CollectorOptions()
{
    //ServerAddress = "hsm.dev.soft-fx.eu",
    AccessKey = "2d19222d-d781-40ee-87e4-3fcaa1e0d711", //local key
    Module = "Collector 3.3.0",
    ComputerName = "LocalMachine(Core)",
};

var _collector = new DataCollector(collectorOptions).AddNLog(new HSMDataCollector.Logging.LoggerOptions() { WriteDebug = true });

//_collector.Windows.AddAllDefaultSensors();

var _baseInt = _collector.CreateIntSensor("instant/int");


await _collector.Start();

CancellationTokenSource tsc = new CancellationTokenSource();

Task.Run(() =>
{
    int i = 0;
    while (true)
    {
        if (tsc.Token.IsCancellationRequested)
            break;

        _baseInt.AddValue(i++, $"Comment {i}");
    }
});
Console.ReadLine();
tsc.Cancel();
tsc.Dispose();


_collector.Dispose();