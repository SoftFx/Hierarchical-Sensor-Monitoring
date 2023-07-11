using HSMDataCollector.Core;
using System.Text;

const string Address = "https://localhost";

const string Key = "1aba29cf-eee8-4376-a72f-5fb01af0f7ff";
const string Path = "/datacollector/file";
const string FileName = "testInstantFileNew";

Random _random = new Random(1234567);


var dataCollector = new DataCollector(Key, Address);
dataCollector.Initialize(useLogging: true);

var fileSensor = dataCollector.CreateFileSensor(Path, fileName: FileName);

while (true)
{
    fileSensor.AddValue(CreateRandomString());

    var key = Console.ReadKey();
    if (key.Key == ConsoleKey.X)
        break;
}

dataCollector.Dispose();


string CreateRandomString()
{
    int capacity = _random.Next(500);

    var sb = new StringBuilder(capacity);

    for (int i = 0; i < capacity; ++i)
        sb.Append(_random.Next(10));

    return sb.ToString();
}