using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using Microsoft.Win32;
using System;

namespace HSMDataCollector.CustomFuncSensor
{
    internal sealed class WindowsUpdateFuncSensor : CustomFuncSensorBase
    {
        private readonly NLog.Logger _logger;
        private readonly TimeSpan _updateInterval;
        private readonly DateTime _windowsLastUpdate;
        private readonly string _windowsVersion;

        public WindowsUpdateFuncSensor(string nodeName, string productKey, IValuesQueue queue,
            string description, TimeSpan timerSpan, SensorType type, bool isLogging, TimeSpan updateInterval)
            : base($"{nodeName ?? TextConstants.PerformanceNodeName}/{TextConstants.WindowsUpdateNodeName}", productKey, queue, description, timerSpan, type)
        {
            _updateInterval = updateInterval;

            //ManagementObject obj = GetManagementObject();
            //TryGetWindowsValue(obj, TextConstants.Version, out _windowsVersion);
            //_windowsLastUpdate = TryGetWindowsValue(obj, TextConstants.InstallDate, out var strDate)
            //    ? ToUTC(strDate) : DateTime.MinValue;

            _windowsVersion = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
            _windowsLastUpdate = GetWindowsInstallationDateTime();

            if (isLogging)
            {
                _logger = Logger.Create(nameof(WindowsUpdateFuncSensor));
            }
        }

        //private static DateTime ToUTC(string str) =>
        //    ManagementDateTimeConverter.ToDateTime(str).ToUniversalTime();

        //private static ManagementObject GetManagementObject()
        //{
        //    var searcher = new ManagementObjectSearcher(TextConstants.Win32OperatingSystem);
        //    ManagementObjectCollection collection = searcher.Get();
        //    if (collection == null)
        //        return null;

        //    foreach (ManagementObject obj in collection)
        //        return obj;

        //    return null;
        //}

        //private bool TryGetWindowsValue(ManagementObject obj, string key, out string value)
        //{
        //    bool isComplete = false;
        //    try
        //    {
        //        value = obj[key].ToString();
        //        isComplete = true;
        //    }
        //    catch (Exception ex)
        //    {
        //        value = string.Empty;
        //        _logger?.Error(ex, $"Failed to get windows {key}");
        //        CreateErrorDataObject(ex);
        //    }

        //    return isComplete;
        //}

        public DateTime GetWindowsInstallationDateTime()
        {
            try
            {
                var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;

                RegistryKey localMachineX64View = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                RegistryKey key = localMachineX64View.OpenSubKey("Software\\Microsoft\\Windows NT\\CurrentVersion");
                //string pathName = (string)registryKey.GetValue("productName");

                //Microsoft.Win32.RegistryKey key = Microsoft.Win32.RegistryKey.OpenRemoteBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Environment.MachineName);
                //key = key.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false);

                if (key != null)
                {
                    //unix add windows ticks
                    DateTime installDate = new DateTime(1970, 1, 1).AddSeconds(Convert.ToInt64($"{key.GetValue("InstallDate")}"));

                    return installDate;
                }

            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to get windows InstallDate");
            }

            return DateTime.MinValue;
        }

        private bool IsVersionNeedUpdate() =>
            DateTime.UtcNow - _windowsLastUpdate >= _updateInterval;

        private string GetDescription() =>
            $"{_windowsVersion} Last Update Date: {_windowsLastUpdate}\n " +
            $"User Description: {Description}";

        protected override SensorValueBase GetInvokeResult() =>
            CreateDataObject(IsVersionNeedUpdate(), GetDescription());

        public override SensorValueBase GetLastValue() =>
            CreateDataObject(IsVersionNeedUpdate(), GetDescription());
    }
}
