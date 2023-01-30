using System;

namespace HSMDataCollector.Options
{
    internal sealed class SensorsDefaultOptions
    {
        internal const string SystemMonitoringNodeName = "System monitoring";
        internal const string CurrentProcessNodeName = "CurrentProcess";
        internal const string DiskMonitoringNodeName = "Disk monitoring";
        internal const string WindowsInfoNodeName = "Windows OS Info";

        private readonly BarSensorOptions _monitoringOptions = new BarSensorOptions(SystemMonitoringNodeName);
        private readonly BarSensorOptions _processOptions = new BarSensorOptions(CurrentProcessNodeName);
        private readonly DiskSensorOptions _diskOptions = new DiskSensorOptions(DiskMonitoringNodeName);
        private readonly WindowsSensorOptions _windowsOptions =
            new WindowsSensorOptions(WindowsInfoNodeName)
            {
                PostDataPeriod = TimeSpan.FromHours(12)
            };
        private readonly SensorOptions _collectorAliveOptions =
            new SensorOptions(SystemMonitoringNodeName)
            {
                PostDataPeriod = TimeSpan.FromSeconds(15)
            };


        internal BarSensorOptions GetSystemMonitoringOptions(BarSensorOptions options) => options ?? _monitoringOptions;

        internal BarSensorOptions GetProcessOptions(BarSensorOptions options) => options ?? _processOptions;

        internal DiskSensorOptions GetDiskMonitoringOptions(DiskSensorOptions options) => options ?? _diskOptions;

        internal WindowsSensorOptions GetWindowsOptions(WindowsSensorOptions options) => options ?? _windowsOptions;

        internal SensorOptions GetCollectorAliveOptions(SensorOptions options) => options ?? _collectorAliveOptions;


        internal BarSensorOptions BuildSystemMonitoringOptions(string path)
        {
            if (path == null)
                path = SystemMonitoringNodeName;

            return new BarSensorOptions(path);
        }

        internal BarSensorOptions BuildCurrentProcessOptions(string path)
        {
            if (path == null)
                path = CurrentProcessNodeName;

            return new BarSensorOptions(path);
        }

        internal WindowsSensorOptions BuildWindowsInfoOptions(string path, TimeSpan? sensorInterval, TimeSpan? updateInterval)
        {
            var options = new WindowsSensorOptions(path ?? WindowsInfoNodeName);

            if (sensorInterval.HasValue)
                options.PostDataPeriod = sensorInterval.Value;
            if (updateInterval.HasValue)
                options.AcceptableUpdateInterval = updateInterval.Value;

            return options;
        }

        internal SensorOptions BuildCollectorAliveOptions(string path)
        {
            if (path == null)
                path = SystemMonitoringNodeName;

            return new SensorOptions(path) { PostDataPeriod = TimeSpan.FromSeconds(15) };
        }
    }
}
