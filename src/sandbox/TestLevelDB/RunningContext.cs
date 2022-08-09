using ConsoleTables;
using System.Diagnostics;
using TestLevelDB.Server;

namespace TestLevelDB
{
    public readonly struct Data
    {
        public int Value { get; init; }

        public long Time { get; init; }
    }


    internal static class RunningContext
    {
        private const int RandomSeed = 1233;
        private const int DelayBetweenTests = 1000;

        private static readonly List<List<string>> _testResults = new(1 << 2);
        private static readonly List<List<string>> _speedResults = new(1 << 2);
        private static readonly List<string> _rowNames = new(1 << 2) { "Test" };

        private static readonly Data[] _sensorData = new Data[Program.TotalDataCnt];

        private static int _testNumber = 0;


        static RunningContext()
        {
            var random = new Random(RandomSeed);

            for (int i = 0; i < Program.TotalDataCnt; ++i)
            {
                var data = new Data()
                {
                    Value = i,
                    Time = DateTime.UtcNow.Ticks,
                };

                _sensorData[i] = data;
            }

            _sensorData = _sensorData.OrderBy(_ => random.Next()).ToArray(); //shuffle data
        }


        public static async Task RunTests(IServer server)
        {
            _testNumber = 0;
            _rowNames.Add(server.Type);

            Console.WriteLine($"Start {server.Type}");

            ClearFolder(server);

            await RunMonitoringTest(OpenDatabase, server);

            await RunMonitoringTest(WriteData, server);

            await RunMonitoringTest(ReadEachFirstSensorData, server);
            await RunMonitoringTest(ReadEachLastSensorData, server);
            await RunMonitoringTest(ReadEachAllSensorData, server);

            //await RunMonitoringTest(CloseDatabase, server);

            Console.WriteLine($"Stop {server.Type}");
            Console.WriteLine();
        }


        private static Task OpenDatabase(IServer server)
        {
            //ClearFolder(server);
            return server.InitServer();
        }

        public static Task CloseDatabase(IServer server)
        {
            return server.CloseDatabase();
        }


        public static Task WriteData(IServer server)
        {
            return server.AddData(_sensorData);
        }

        public static Task ReadEachFirstSensorData(IServer server)
        {
            return server.ReadEachFirstSensorData();
        }

        public static Task ReadEachLastSensorData(IServer server)
        {
            return server.ReadEachLastSensorData();
        }

        public static Task ReadEachAllSensorData(IServer server)
        {
            return server.ReadEachAllSensorData();
        }


        public static void PrintStats()
        {
            Console.WriteLine($"Total sensors cnt = {Program.SensorsCnt}");
            Console.WriteLine($"Total data cnt = {Program.TotalDataCnt}");

            PrintTable(_testResults);
            PrintTable(_speedResults);
        }

        private static async Task RunMonitoringTest(Func<IServer, Task> test, IServer server)
        {
            var testName = test.Method.Name;
            var st = new Stopwatch();

            if (_testNumber == _testResults.Count)
                _testResults.Add(new List<string>(1 << 2) { testName });

            Console.WriteLine();
            Console.WriteLine($"Test {testName} is running");

            st.Start();

            await test(server);

            st.Stop();

            await Task.Delay(DelayBetweenTests);

            var sec = st.ElapsedMilliseconds / 1000.0;

            if (testName == nameof(WriteData))
                AddSpeedData(0, st.ElapsedMilliseconds, "Write speed");

            if (testName == nameof(ReadEachAllSensorData))
                AddSpeedData(1, st.ElapsedMilliseconds, "Read speed");

            _testResults[_testNumber++].Add($"{sec} sec");

            Console.WriteLine($"Test {testName} stoped. Total {sec} sec");
        }


        private static void AddSpeedData(int index, double value, string header)
        {
            if (_speedResults.Count == index)
                _speedResults.Add(new List<string>(1 << 2) { header });

            value = value < 1e-9 ? 1.0 : value;

            _speedResults[index].Add($"{Program.TotalDataCnt / value * 1000.0:F2} d/sec");
        }

        private static void PrintTable(List<List<string>> data)
        {
            var table = new ConsoleTable(_rowNames.ToArray());

            foreach (var row in data)
                table.AddRow(row.ToArray());

            table.Write(Format.Alternative);
        }

        private static void ClearFolder(IServer server)
        {
            var folderPath = Path.Combine(Environment.CurrentDirectory, server.Type);

            if (Directory.Exists(folderPath))
                Directory.Delete(folderPath, true);

            Directory.CreateDirectory(folderPath);
        }
    }
}