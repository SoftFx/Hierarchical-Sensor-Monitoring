using HSMDataCollector.Core;

namespace HSMDataCollector.Extensions
{
    internal static class DataCollectorExtensions
    {
        public static bool IsStopped(this CollectorStatus status)
        {
            return status == CollectorStatus.Stopped;
        }

        public static bool IsRunning(this CollectorStatus status)
        {
            return status == CollectorStatus.Running;
        }
    }
}