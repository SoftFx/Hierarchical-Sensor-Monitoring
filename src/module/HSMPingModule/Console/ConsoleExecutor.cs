using NLog;
using System.Diagnostics;
using System.Text;

namespace HSMPingModule.Console
{
    internal static class ConsoleExecutor
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private static readonly StreamWriter _commandWriter;
        private static readonly Process _cli;


        //static ConsoleExecutor()
        //{
        //    //try
        //    //{
        //    //    _cli = new Process()
        //    //    {
        //    //        StartInfo = new ProcessStartInfo("/bin/bash")
        //    //        {
        //    //            RedirectStandardInput = true,
        //    //            RedirectStandardOutput = true,
        //    //            UseShellExecute = false,
        //    //            CreateNoWindow = true,
        //    //        },
        //    //    };

        //    //    _cli.Start();

        //    //    _commandWriter = _cli.StandardInput;
        //    //}
        //    //catch (Exception ex)
        //    //{
        //    //    _logger.Error(ex);
        //    //}
        //}


        public static async Task<string> Run(string command, HashSet<string> skipOutput = null)
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

            //await Task.Delay(5000); // TODO should be changed to disconnect task

            var reader = process.StandardOutput;

            //var result = await process.StandardOutput.ReadToEndAsync();

            var sb = new StringBuilder(1 << 5);

            while (!reader.EndOfStream)
            {
                var line = (await reader.ReadLineAsync()).Trim();

                if (!string.IsNullOrEmpty(line) && !(skipOutput?.Contains(line) ?? false))
                {
                    sb.AppendLine(line);

                    _logger.Debug(line);
                }

                await Task.Delay(100);
            }


            //await Task.Delay(5000); // TODO should be changed to disconnect task

            await process.WaitForExitAsync();

            return sb.ToString();

            //if (_commandWriter.BaseStream.CanWrite)
            //{
            //    _commandWriter.WriteLine($"-c ({command})");

            //    await Task.Delay(5000); // TODO should be changed to disconnect task

            //    var result = await _cli.StandardOutput.ReadToEndAsync();

            //    _logger.Debug(result);

            //    return result;
            //}
            //else
            //    _logger.Error("Cannot write command to cli");

            //return string.Empty;

            //var cli = new Process()
            //{
            //    StartInfo = new ProcessStartInfo()
            //    {
            //        FileName = "/bin/bash",
            //        Arguments = $"-c '{command}'",
            //        RedirectStandardOutput = true,
            //        UseShellExecute = false,
            //        CreateNoWindow = true,
            //    },
            //};

            //cli.Start();

            //await Task.Delay(5000); // TODO should be changed to disconnect task

            //var result = await cli.StandardOutput.ReadToEndAsync();

            //_logger.Debug(result);

            //await cli.WaitForExitAsync();

            //return result;

            ////if (_commandWriter.BaseStream.CanWrite)
            ////{
            ////    _commandWriter.WriteLine($"-c ({command})");

            ////    await Task.Delay(5000); // TODO should be changed to disconnect task

            ////    var result = await _cli.StandardOutput.ReadToEndAsync();

            ////    _logger.Debug(result);

            ////    return result;
            ////}
            ////else
            ////    _logger.Error("Cannot write command to cli");

            ////return string.Empty;
        }

        public static Task Stop()
        {
            _commandWriter.Dispose();
            return _cli.WaitForExitAsync();
        }
    }
}