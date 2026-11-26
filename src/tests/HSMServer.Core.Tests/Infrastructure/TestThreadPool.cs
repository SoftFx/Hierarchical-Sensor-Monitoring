using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace HSMServer.Core.Tests.Infrastructure
{
    /// <summary>
    /// Starts the test host with enough thread pool threads for the parallel run (#1943).
    /// Dozens of test classes start at once, and many of them block a pool thread for a while
    /// (a sync wait on a drain, a real LevelDB write); with the default minimum (one thread per
    /// core) the pool then grows by injection, about two threads a second, and work items queue
    /// meanwhile: measured, 3 -> 48 threads over the first 22 s with up to 50 items waiting. The
    /// product's bounded waits on its own background loops (the security-event sink's 3 s drain,
    /// UpdatesQueue.DisposeAsync's 5 s) then expired on a loaded machine. With the threads there
    /// from the start no work item waits for one.
    /// </summary>
    internal static class TestThreadPool
    {
        private const int MinThreads = 128;


        [ModuleInitializer]
        internal static void Raise()
        {
            ThreadPool.GetMinThreads(out var workers, out var io);
            ThreadPool.SetMinThreads(Math.Max(workers, MinThreads), Math.Max(io, MinThreads));
        }
    }
}
