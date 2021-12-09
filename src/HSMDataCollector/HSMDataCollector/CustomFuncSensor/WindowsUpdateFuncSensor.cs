using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System;
using System.Management;

namespace HSMDataCollector.CustomFuncSensor
{
    internal class WindowsUpdateFuncSensor : CustomFuncSensorBase
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
            TryGetWindowsVersion(obj, out _windowsVersion);
            TryGetWindowsUpdateDate(obj, out _windowsLastUpdate);

           //check windows ?
            if (isLogging)
            {
                _logger = Logger.Create(nameof(WindowsUpdateFuncSensor));
            }
        }

        private ManagementObject GetManagementObject()
        {
            var searcher = new ManagementObjectSearcher(TextConstants.Win32OperatingSystem);
            ManagementObjectCollection collection = searcher.Get();
            if (collection == null) return null;

            foreach (ManagementObject obj in collection)
                return obj;

            return null;
        }

        private bool TryGetWindowsVersion(ManagementObject obj, out string windowsVersion)
        {
            bool isComplete = false;
            try
            {
                windowsVersion = obj[TextConstants.Version].ToString();
                isComplete = true;
            }
            catch(Exception ex)
            {
                windowsVersion = string.Empty;
                _logger?.Error(ex, "Failed to get windows version.");
                CreateErrorDataObject(ex);
            }

            return isComplete;
        }

        private bool TryGetWindowsUpdateDate(ManagementObject obj, out DateTime updateDate)
        {
            bool isComplete = false;
            try
            {
                updateDate = ManagementDateTimeConverter.ToDateTime(obj[TextConstants.InstallDate].ToString());
                isComplete = true;
            }
            catch (Exception ex)
            {
                updateDate = DateTime.MinValue;
                _logger?.Error(ex, "Failed to get windows update date.");
                CreateErrorDataObject(ex);
            }

            return isComplete;
        }

        private bool IsVersionNeedUpdate() => 
            DateTime.UtcNow - _windowsLastUpdate >= _updateInterval;

        private UnitedSensorValue CreateDataObject(bool value)
        {
            var valueObject = new UnitedSensorValue();

            valueObject.Data = value.ToString();
            valueObject.Description = GetDescription();
            valueObject.Path = Path;
            valueObject.Key = ProductKey;
            valueObject.Time = DateTime.Now;
            valueObject.Type = Type;

            return valueObject;
        }

        private string GetDescription() =>
            $"Windows Version: {_windowsVersion} Last Update Date: {_windowsLastUpdate}\n " +
            $"User Description: {Description}";

        protected override UnitedSensorValue GetInvokeResult() =>
            CreateDataObject(IsVersionNeedUpdate());


        public override UnitedSensorValue GetLastValue() =>
            CreateDataObject(IsVersionNeedUpdate());
    }
}
