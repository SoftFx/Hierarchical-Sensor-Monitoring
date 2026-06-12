using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors
{
    internal static class ProcessInfo
    {
        internal static Process CurrentProcess => Process.GetCurrentProcess();

        internal static string CurrentProcessName => CurrentProcess.ProcessName;

        internal static int CurrentProcessId => CurrentProcess.Id;


        internal static Process GetPowershellProcess(string args)
        {
            return new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = "powershell.exe",
                    Arguments = args.Replace("\"", "\\\""),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
        }
    }
}
