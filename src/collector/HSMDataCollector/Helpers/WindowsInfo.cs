using HSMDataCollector.Logging;
using Microsoft.Win32;
using System;

namespace HSMDataCollector.Helpers
{
    internal static class WindowsInfo
    {
        private static readonly NLog.Logger _logger = Logger.Create(nameof(WindowsInfo));


        internal static DateTime GetInstallationDate()
        {
            try
            {
                var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;

                RegistryKey localMachineX64View = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                RegistryKey key = localMachineX64View.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion");

                if (key != null)
                    return new DateTime(1970, 1, 1).AddSeconds(Convert.ToInt64($"{key.GetValue("InstallDate")}"));  //add windows ticks to unix timestamp
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to get windows InstallDate");
            }

            return DateTime.MinValue;
        }
    }
}
