using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using Microsoft.Win32;
using System;

namespace HSMDataCollector.CustomFuncSensor
{
    internal sealed class WindowsUpdateFuncSensor : CustomFuncSensorBase
    {
        private const string WindowsUpdateNodeName = "Is need Windows update";

        private readonly NLog.Logger _logger;
        private readonly TimeSpan _updateInterval;
        private readonly DateTime _windowsLastUpdate;
        private readonly string _windowsVersion;


        public WindowsUpdateFuncSensor(string nodeName, IValuesQueue queue,
            string description, TimeSpan timerSpan, SensorType type, bool isLogging, TimeSpan updateInterval)
            : base($"{nodeName ?? DataCollector.PerformanceNodeName}/{WindowsUpdateNodeName}", queue, description, timerSpan, type)
        {
            _updateInterval = updateInterval;

            _windowsVersion = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
            _windowsLastUpdate = GetWindowsInstallationDateTime();

            if (isLogging)
            {
                _logger = Logger.Create(nameof(WindowsUpdateFuncSensor));
            }
        }


        public override SensorValueBase GetLastValue() =>
            CreateDataObject(IsVersionNeedUpdate(), GetDescription());

        protected override SensorValueBase GetInvokeResult() =>
            CreateDataObject(IsVersionNeedUpdate(), GetDescription());

        private DateTime GetWindowsInstallationDateTime()
        {
            try
            {
                var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;

                RegistryKey localMachineX64View = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                RegistryKey key = localMachineX64View.OpenSubKey("Software\\Microsoft\\Windows NT\\CurrentVersion");

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
    }
}
