using HSMDataCollector.DefaultSensors;
using System;

namespace HSMDataCollector.Extensions
{
    internal static class BashCommandExtension
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        internal static string BashExecute(this string command)
        {
            using (var process = ProcessInfo.GetProcess(command))
            {
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit((int)DefaultTimeout.TotalMilliseconds))
                {
                    TryKill(process);
                    throw new TimeoutException($"Bash command timed out after {DefaultTimeout.TotalSeconds} seconds: {command}");
                }

                var error = errorTask.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"Bash command failed with exit code {process.ExitCode}: {error}");

                return outputTask.GetAwaiter().GetResult();
            }
        }

        private static void TryKill(System.Diagnostics.Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch
            {
            }
        }
    }
}
