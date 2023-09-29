using NLog;
using System;
using System.Diagnostics;
using System.Text;

namespace HSMPingModule.Console
{
    internal static class ConsoleExecutor
    {
        private const int MaxRequestDelay = 10;

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private static readonly StringBuilder _sb = new(1 << 5);


        public static async Task<string> Run(string command, HashSet<string> skipOutput = null)
        {
            try
            {
                _logger.Info($"Run command: {command}");

                using var process = new Process()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"-c \"{command}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };

                process.Start();

                var reader = process.StandardOutput;
                var longTaskCancel = new CancellationTokenSource();

                _sb.Clear();

                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(MaxRequestDelay));

                    _logger.Info($"Send cancel request");

                    longTaskCancel.Cancel();
                });

                while (!reader.EndOfStream)
                {
                    var line = (await reader.ReadLineAsync(longTaskCancel.Token)).Trim();

                    if (!string.IsNullOrEmpty(line) && !(skipOutput?.Contains(line) ?? false))
                    {
                        _sb.AppendLine(line);

                        _logger.Debug(line);
                    }

                    if (longTaskCancel.IsCancellationRequested)
                        break;

                    await Task.Delay(100);
                }


                return _sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }

            return string.Empty;
        }
    }
}