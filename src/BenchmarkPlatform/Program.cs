using BenchmarkDotNet.Running;

namespace BenchmarkPlatform
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //var b = new SyncQueuePerformanceTest();
            //await b.SyncQueue();
            BenchmarkRunner.Run<SyncQueuePerformanceTest>();
        }
    }
}
