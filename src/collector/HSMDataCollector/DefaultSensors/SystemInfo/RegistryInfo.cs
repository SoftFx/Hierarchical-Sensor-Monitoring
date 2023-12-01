using Microsoft.Win32;
using System;
using System.Text;

namespace HSMDataCollector.DefaultSensors
{
    internal static class RegistryInfo
    {
        private const string WindowsOSNodeInfo = @"Software\Microsoft\Windows NT\CurrentVersion";

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
                var unixStartTime = new DateTime(1970, 1, 1);
                return unixStartTime.AddSeconds(Convert.ToInt64($"{node.GetValue("InstallDate")}"));
            }

            return DateTime.MinValue;
        }

        internal static string GetCurrentWindowsProductName() =>
            TryLoadWindowsOsNode(out var node) ? $"{node.GetValue("ProductName")}" : null;

        internal static string GetCurrentWindowsDisplayVersion() =>
            TryLoadWindowsOsNode(out var node) ? $"{node.GetValue("DisplayVersion")}" : null;

        internal static string GetCurrentWindowsFullBuildVersion()
        {
            if (TryLoadWindowsOsNode(out var node))
            {
                var sb = new StringBuilder(1 << 4);

                return sb.Append(node.GetValue("CurrentMajorVersionNumber"))
                         .Append('.')
                         .Append(node.GetValue("CurrentMinorVersionNumber"))
                         .Append('.')
                         .Append(node.GetValue("CurrentBuildNumber"))
                         .ToString();
            }

            return null;
        }


        private static bool TryLoadWindowsOsNode(out RegistryKey node)
        {
            node = _localMachineKey?.OpenSubKey(WindowsOSNodeInfo);

            return node != null;
        }
    }
}
