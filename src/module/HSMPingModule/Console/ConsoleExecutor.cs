using System.Diagnostics;

namespace HSMPingModule.Console
{
    internal static class ConsoleExecutor
    {
        public static async Task<string> Run(string command)
        {
            using var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c {command}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            process.Start();

            var result = await process.StandardOutput.ReadToEndAsync();

            await Task.Delay(5000); // TODO should be changed to disconnect task

            await process.WaitForExitAsync();

            return result;
        }
    }
}