using System;
using System.Reflection;
using HSMDataCollector.Core;


namespace HSMDataCollector.Extensions
{
    internal static class DataCollectorExtensions
    {
        internal static Version Version { get; }


        static DataCollectorExtensions()
        {
            var assembly = Assembly.GetExecutingAssembly()?.GetName();
            Version = assembly.Version;
        }


        public static bool IsStopped(this CollectorStatus status)
        {
            return status == CollectorStatus.Stopped;
        }

        public static bool IsStartingOrRunning(this CollectorStatus status)
        {
            return status == CollectorStatus.Running || status == CollectorStatus.Starting;
        }

        public static bool IsRunning(this CollectorStatus status)
        {
            return status == CollectorStatus.Running;
        }
    }
}