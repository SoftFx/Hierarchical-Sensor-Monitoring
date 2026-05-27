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
                    ObserveReadTasks(outputTask, errorTask);
                    throw new TimeoutException($"Bash command timed out after {DefaultTimeout.TotalSeconds} seconds: {command}");
                }

                process.WaitForExit();

                var output = outputTask.GetAwaiter().GetResult();
                var error  = errorTask.GetAwaiter().GetResult();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"Bash command failed with exit code {process.ExitCode}: {error}");

                return output;
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

        private static void ObserveReadTasks(params System.Threading.Tasks.Task[] tasks)
        {
            foreach (var task in tasks)
            {
                task.ContinueWith(t => { var _ = t.Exception; },
                                  System.Threading.CancellationToken.None,
                                  System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted | System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously,
                                  System.Threading.Tasks.TaskScheduler.Default);
            }

            try
            {
                System.Threading.Tasks.Task.WaitAll(tasks, TimeSpan.FromSeconds(1));
            }
            catch
            {
            }
        }
    }
}
