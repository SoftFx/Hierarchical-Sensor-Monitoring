using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System;
using System.Management;

namespace HSMDataCollector.CustomFuncSensor
{
    internal sealed class WindowsUpdateFuncSensor : CustomFuncSensorBase
    {
        private readonly NLog.Logger _logger;
        private readonly TimeSpan _updateInterval;
        private readonly DateTime _windowsLastUpdate;
        private readonly string _windowsVersion;

        public WindowsUpdateFuncSensor(string path, string productKey, IValuesQueue queue,
            string description, TimeSpan timerSpan, SensorType type, bool isLogging, TimeSpan updateInterval) 
            : base(path, productKey, queue, description, timerSpan, type)
        {
            _updateInterval = updateInterval;

            ManagementObject obj = GetManagementObject();
            TryGetWindowsValue(obj, TextConstants.Version, out _windowsVersion);
            _windowsLastUpdate = TryGetWindowsValue(obj, TextConstants.InstallDate, out var strDate) 
                ? ToUTCDateTime(strDate) : DateTime.MinValue;

            if (isLogging)
            {
                _logger = Logger.Create(nameof(WindowsUpdateFuncSensor));
            }
        }

        private static DateTime ToUTCDateTime(string str) => 
            ManagementDateTimeConverter.ToDateTime(str).ToUniversalTime();

        private static ManagementObject GetManagementObject()
        {
            var searcher = new ManagementObjectSearcher(TextConstants.Win32OperatingSystem);
            ManagementObjectCollection collection = searcher.Get();
            if (collection == null) 
                return null;

            foreach (ManagementObject obj in collection)
                return obj;

            return null;
        }

        private bool TryGetWindowsValue(ManagementObject obj, string key, out string value)
        {
            bool isComplete = false;
            try
            {
                value = obj[key].ToString();
                isComplete = true;
            }
            catch(Exception ex)
            {
                value = string.Empty;
                _logger?.Error(ex, $"Failed to get windows {key}");
                CreateErrorDataObject(ex);
            }

            return isComplete;
        }

        private bool IsVersionNeedUpdate() => 
            DateTime.UtcNow - _windowsLastUpdate >= _updateInterval;

        private string GetDescription() =>
            $"Windows Version: {_windowsVersion} Last Update Date: {_windowsLastUpdate}\n " +
            $"User Description: {Description}";

        protected override UnitedSensorValue GetInvokeResult() =>
            CreateDataObject(IsVersionNeedUpdate(), GetDescription());

        public override UnitedSensorValue GetLastValue() =>
            CreateDataObject(IsVersionNeedUpdate(), GetDescription());
    }
}
