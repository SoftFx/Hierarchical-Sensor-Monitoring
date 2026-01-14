using BenchmarkDotNet.Running;
using PerformanceBenchmarks;

namespace BenchmarkPlatform
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // BenchmarkRunner.Run<SerializationSpeedBenchmarks>();
            var test = new SerializationSpeedBenchmarks();
            test.DataSize = 1000;
            test.Setup();
            test.Cleanup();
        }
    }
}
