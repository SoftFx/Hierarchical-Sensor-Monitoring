using HSMDataCollector.DefaultSensors;

namespace HSMDataCollector.Extensions
{
    internal static class BashCommandExtension
    {
        internal static string BashExecute(this string command)
        {
            using (var process = ProcessInfo.GetProcess(command))
            {
                process.Start();

                string result = process.StandardOutput.ReadToEnd();

                process.WaitForExit();

                return result;
            }
        }
    }
}
