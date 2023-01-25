using HSMDataCollector.Logging;
using Microsoft.Win32;
using System;

namespace HSMDataCollector.Helpers
{
    internal static class WindowsInfo
    {
        private static readonly NLog.Logger _logger = Logger.Create(nameof(WindowsInfo));

        private static readonly RegistryView _view;
        private static readonly RegistryKey _localMachineKey;


        static WindowsInfo()
        {
            _view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
            _localMachineKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, _view);
        }


        internal static DateTime GetInstallationDate()
        {
            try
            {
                RegistryKey key = _localMachineKey.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion");

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
