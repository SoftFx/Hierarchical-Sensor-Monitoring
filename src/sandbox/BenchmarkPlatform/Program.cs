using BenchmarkDotNet.Running;
using TestLevelDB;

namespace BenchmarkPlatform
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //var b = new SyncQueuePerformanceTest();
            //await b.SyncQueue();
            //BenchmarkRunner.Run<SyncQueuePerformanceTest>();
            BenchmarkRunner.Run<ByteKeyConverters>();
        }
    }
}
