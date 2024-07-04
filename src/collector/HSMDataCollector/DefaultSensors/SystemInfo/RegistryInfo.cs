using System;
using Microsoft.Win32;


namespace HSMDataCollector.DefaultSensors
{
    internal static class RegistryInfo
    {
        private const string WindowsOSNodeInfo = @"Software\Microsoft\Windows NT\CurrentVersion";
        private const string WindowsOSUpdateNodeInfo = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate";

        private static readonly RegistryView _view;
        private static readonly RegistryKey _localMachineKey;


        static RegistryInfo()
        {
            _view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
            _localMachineKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, _view);
        }


        internal static DateTime GetInstallationDate()
        {
            if (TryLoadWindowsOsNode(out var node))
            {
                var timestamp = (long?)node.GetValue("InstallTime");
                if (timestamp != null)
                    return DateTime.FromFileTime(timestamp.Value).ToUniversalTime();
            }

            return DateTime.MinValue;
        }

        internal static string GetCurrentWindowsProductName() =>
            TryLoadWindowsOsNode(out var node) ? $"{node.GetValue("ProductName")}" : null;

        internal static string GetCurrentWindowsDisplayVersion() =>
            TryLoadWindowsOsNode(out var node) ? $"{node.GetValue("DisplayVersion")}" : null;

        internal static Version GetCurrentWindowsFullBuildVersion()
        {
            if (TryLoadWindowsOsNode(out var node))
            {
                int GetInt(string key) => int.Parse(node.GetValue(key).ToString());

                return new Version(GetInt("CurrentMajorVersionNumber"), GetInt("CurrentMinorVersionNumber"), GetInt("CurrentBuildNumber"));
            }

            return null;
        }


        private static bool TryLoadWindowsOsNode(out RegistryKey node)
        {
            node = _localMachineKey?.OpenSubKey(WindowsOSNodeInfo);

            return node != null;
        }
        
        private static bool TryLoadWindowsOsUpdateNode(out RegistryKey node)
        {
            node = _localMachineKey?.OpenSubKey(WindowsOSUpdateNodeInfo);

            return node != null;
        }
    }
}
