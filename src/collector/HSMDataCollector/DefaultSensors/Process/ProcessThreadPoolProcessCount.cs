using System.Threading;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors
{
    internal sealed class ProcessThreadPoolThreadCount : CollectableBarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        internal ProcessThreadPoolThreadCount(BarSensorOptions options) : base(options) { }


        protected override double? GetBarData()
        {
            ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxCompletionPortThreads);

            ThreadPool.GetAvailableThreads(out int availableWorkerThreads, out int availableCompletionPortThreads);

            int workerThreadsInUse = maxWorkerThreads - availableWorkerThreads;
            int completionPortThreadsInUse = maxCompletionPortThreads - availableCompletionPortThreads;

            return workerThreadsInUse + completionPortThreadsInUse;
        }
    }
}
