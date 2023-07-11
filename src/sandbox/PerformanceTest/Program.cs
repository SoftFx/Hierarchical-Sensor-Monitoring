using System;
using System.Threading.Tasks;

namespace PerformanceTest
{
    internal static class Program
    {
        internal const string Address = "https://localhost";
        internal const int Port = 44330;

        private static async Task Main(string[] _)
        {
            //HttpClientTest.Start();
            await DataCollectorTest.Start();

            Console.ReadKey();
        }
    }
}
