using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BenchmarkPlatform.ReverseBenh
{
    [MemoryDiagnoser]
    public class ReverseTest
    {
        private const int DataCount = 1_000_000;

        private readonly ConcurrentQueue<int> _values = new();


        public ReverseTest()
        {
            for (int i = 0; i < DataCount; ++i)
                _values.Enqueue(i);
        }


        [Benchmark]
        public void HandleRevers()
        {
            var data = new List<int>(DataCount);
            var rev = _values.ToList();

            for (int i = DataCount - 1; i >= 0; --i)
                data.Add(rev[i]);
        }


        [Benchmark]
        public void EnumRevers()
        {
            var data = new List<int>(DataCount);
            var rev = _values.Reverse();

            foreach (var v in rev)
            {
                data.Add(v);
            }
        }
    }
}
