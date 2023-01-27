using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors
{
    internal static class ProcessInfo
    {
        internal static Process CurrentProcess { get; } = Process.GetCurrentProcess();

        internal static string CurrentProcessName => CurrentProcess.ProcessName;
    }
}
