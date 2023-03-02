using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors
{
    internal static class ProcessInfo
    {
        internal static Process CurrentProcess => Process.GetCurrentProcess();

        internal static string CurrentProcessName => CurrentProcess.ProcessName;


        internal static Process GetProcess(string args)
        {
            return new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{args.Replace("\"", "\\\"")}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
        }
    }
}
