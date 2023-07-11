using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PerformanceTest
{
    internal sealed class DataCollectorTest
    {
        private const string Key = "f05370e2-34ea-490a-b4f6-a5d9f8f720a2";

        private const int MaxSensorsPerLevel = 1000;
        private const int MaxNodesPerLevel = 10;
        private const int SensorsCount = 50000;

        private readonly List<IInstantValueSensor<double>> _sensors = new(SensorsCount);
        private readonly List<int> _nodesPerLevel = new(1 << 2);

        private readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(5);
        private readonly CancellationTokenSource _token = new();

        private DataCollector _collector;


        internal static Task Start() => new DataCollectorTest().StartTest();

        private async Task StartTest()
        {
            var options = new CollectorOptions()
            {
                AccessKey = Key,
                ServerAddress = Program.Address,
                Port = Program.Port,
                MaxQueueSize = 3 * SensorsCount,
                MaxValuesInPackage = 10000,
                PackageCollectPeriod = DefaultDelay,
            };

            _collector = new DataCollector(options);

            BuildSensors();

            await _collector.Start();

            _ = AddValues();

            Console.ReadKey();

            _token.Cancel();
        }

        private async Task AddValues()
        {
            var rand = new Random(134134278);

            while (!_token.IsCancellationRequested)
            {
                foreach (var sensor in _sensors)
                    sensor.AddValue(rand.NextDouble());

                await Task.Delay(DefaultDelay);
            }

            _collector.Dispose();
        }

        private void BuildSensors()
        {
            void AddNodesPerLevel(int count) => _nodesPerLevel.Add(Math.Min(count, MaxNodesPerLevel));


            var curLevel = SensorsCount / MaxSensorsPerLevel;
            AddNodesPerLevel(curLevel);

            while (curLevel / MaxNodesPerLevel > 0)
            {
                curLevel /= MaxNodesPerLevel;
                AddNodesPerLevel(curLevel);
            }

            _nodesPerLevel.Reverse();

            BuildSensors(0, string.Empty);
        }

        private void BuildSensors(int index, string path)
        {
            if (index < _nodesPerLevel.Count)
                for (int i = 0; i < _nodesPerLevel[index]; ++i)
                    BuildSensors(index + 1, $"{path}/{i}");
            else
                for (int i = 0; i < MaxSensorsPerLevel; ++i)
                    _sensors.Add(_collector.CreateDoubleSensor($"{path}/sensor{i}", null));
        }
    }
}
