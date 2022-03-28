using BenchmarkDotNet.Attributes;
using BenchmarkPlatform.SyncQueuePerformance;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BenchmarkPlatform
{
    [MemoryDiagnoser]
    public class SyncQueuePerformanceTest
    {
        private const int DataCount = 1_000_000;
        private const int GeneratorSeed = 41231;

        private readonly List<int> _rawData;


        public SyncQueuePerformanceTest()
        {
            var random = new Random(GeneratorSeed);

            _rawData = new List<int>(DataCount);

            for (int i = 0; i < DataCount; i++)
                _rawData.Add(random.Next(-100, 100));
        }

        [Benchmark]
        public async Task ConcurrentQueue()
        {
            var data = new List<int>(DataCount);
            var queue = new ConcurrentQueue();

            queue.NewItemEvent += d => data.Add(d);

            foreach (int d in _rawData)
                queue.AddItem(d);

            while (data.Count != DataCount)
                await Task.Delay(10);
        }

        [Benchmark]
        public async Task LockQueue()
        {
            var data = new List<int>(DataCount);
            var queue = new LockQueue();

            queue.NewItemEvent += d => data.Add(d);

            foreach (int d in _rawData)
                queue.AddItem(d);

            while (data.Count != DataCount)
                await Task.Delay(10);
        }

        [Benchmark]
        public async Task SyncQueue()
        {
            var data = new List<int>(DataCount);
            var queue = new SyncQueue();

            queue.NewItemEvent += d => data.Add(d);

            foreach (int d in _rawData)
                queue.AddItem(d);

            while (data.Count != DataCount)
                await Task.Delay(10);
        }
    }
}
