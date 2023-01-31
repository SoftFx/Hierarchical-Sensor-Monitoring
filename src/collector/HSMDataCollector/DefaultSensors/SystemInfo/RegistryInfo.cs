using HSMDataCollector.Logging;
using Microsoft.Win32;
using System;

namespace HSMDataCollector.DefaultSensors
{
    internal static class RegistryInfo
    {
        private static readonly NLog.Logger _logger = Logger.Create(nameof(RegistryInfo));

        private static readonly RegistryView _view;
        private static readonly RegistryKey _localMachineKey;


        static RegistryInfo()
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
                {
                    var unixStartTime = new DateTime(1970, 1, 1);
                    return unixStartTime.AddSeconds(Convert.ToInt64($"{key.GetValue("InstallDate")}"));
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to get windows InstallDate");
            }

            return DateTime.MinValue;
        }
    }
}
