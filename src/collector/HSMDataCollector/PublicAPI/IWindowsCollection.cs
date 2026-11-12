using System;
using HSMDataCollector.Options;


namespace HSMDataCollector.PublicInterface
{
    public interface IWindowsCollection : IDisposable
    {
        IWindowsCollection AddAllComputerSensors();

        IWindowsCollection AddAllModuleSensors(Version productVersion = null);

        IWindowsCollection AddAllDefaultSensors(Version productVersion = null);


        IWindowsCollection AddProcessCpu(BarSensorOptions options = null);

        IWindowsCollection AddProcessMemory(BarSensorOptions options = null);

        IWindowsCollection AddProcessTimeInGC(BarSensorOptions options = null);

        IWindowsCollection AddProcessThreadCount(BarSensorOptions options = null);

        IWindowsCollection AddProcessThreadPoolThreadCount(BarSensorOptions options = null);

        IWindowsCollection AddProcessMonitoringSensors(BarSensorOptions options = null);


        IWindowsCollection AddTotalCpu(BarSensorOptions options = null);

        IWindowsCollection AddFreeRamMemory(BarSensorOptions options = null);

        IWindowsCollection AddGlobalTimeInGC(BarSensorOptions options = null);

        IWindowsCollection AddSystemMonitoringSensors(BarSensorOptions options = null);


        IWindowsCollection AddFreeDiskSpace(DiskSensorOptions options = null);

        IWindowsCollection AddFreeDiskSpacePrediction(DiskSensorOptions options = null);

        IWindowsCollection AddFreeDisksSpace(DiskSensorOptions options = null);

        IWindowsCollection AddFreeDisksSpacePrediction(DiskSensorOptions options = null);

        IWindowsCollection AddActiveDiskTime(DiskBarSensorOptions options = null);

        IWindowsCollection AddActiveDisksTime(DiskBarSensorOptions options = null);

        IWindowsCollection AddDiskQueueLength(DiskBarSensorOptions options = null);

        IWindowsCollection AddDisksQueueLength(DiskBarSensorOptions options = null);

        IWindowsCollection AddDiskAverageWriteSpeed(DiskBarSensorOptions options = null);

        IWindowsCollection AddDisksAverageWriteSpeed(DiskBarSensorOptions options = null);

        IWindowsCollection AddDiskMonitoringSensors(DiskSensorOptions options = null, DiskBarSensorOptions diskBarOptions = null);

        IWindowsCollection AddAllDisksMonitoringSensors(DiskSensorOptions options = null, DiskBarSensorOptions diskBarOptions = null);

        IWindowsCollection AddWindowsLastUpdate(WindowsInfoSensorOptions options = null);

        IWindowsCollection AddWindowsLastRestart(WindowsInfoSensorOptions options = null);

        IWindowsCollection AddWindowsInstallDate(WindowsInfoSensorOptions options = null);

        IWindowsCollection AddWindowsVersion(WindowsInfoSensorOptions options = null);

        IWindowsCollection AddWindowsApplicationErrorLogs(InstantSensorOptions options = null);

        IWindowsCollection AddWindowsSystemErrorLogs(InstantSensorOptions options = null);

        IWindowsCollection AddErrorWindowsLogs(InstantSensorOptions options = null);

        IWindowsCollection AddWindowsApplicationWarningLogs(InstantSensorOptions options = null);

        IWindowsCollection AddWindowsSystemWarningLogs(InstantSensorOptions options = null);

        IWindowsCollection AddWarningWindowsLogs(InstantSensorOptions options = null);

        IWindowsCollection AddAllWindowsLogs(InstantSensorOptions options = null);

        IWindowsCollection AddWindowsInfoMonitoringSensors(WindowsInfoSensorOptions infoOptions = null, InstantSensorOptions logsOptions = null);


        IWindowsCollection AddCollectorAlive(CollectorMonitoringInfoOptions options = null);

        IWindowsCollection AddCollectorVersion();

        IWindowsCollection AddCollectorErrors();

        IWindowsCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptions options = null);


        IWindowsCollection AddProductVersion(VersionSensorOptions options);


        IWindowsCollection AddQueuePackageContentSize(BarSensorOptions options = null);

        IWindowsCollection AddQueuePackageProcessTime(BarSensorOptions options = null);

        IWindowsCollection AddQueuePackageValuesCount(BarSensorOptions options = null);

        IWindowsCollection AddQueueOverflow(BarSensorOptions options = null);

        IWindowsCollection AddAllQueueDiagnosticSensors(BarSensorOptions barOptions = null);


        IWindowsCollection AddNetworkConnectionsEstablished(NetworkSensorOptions options = null);

        IWindowsCollection AddNetworkConnectionFailures(NetworkSensorOptions options = null);

        IWindowsCollection AddNetworkConnectionsReset(NetworkSensorOptions options = null);

        /// <summary>
        /// Enable per-interface network speed monitoring. Samples received/sent byte counters on all
        /// active (Up, non-loopback) network interfaces every 10 seconds and posts Double bar sensors at
        /// "Network/&lt;interface-name&gt;/{Received,Sent} MB,sec" (computer-level sensors, 1-minute bars).
        /// A disappeared interface expires by TTL (5 min). Windows only.
        /// </summary>
        IWindowsCollection AddNetworkInterfacesSpeed();

        /// <summary>
        /// Adds the rate of failed TCP connection attempts, ".computer/Network/Connection failures rate":
        /// every <paramref name="period"/> (1 minute by default) the increase of the failed-attempt
        /// counter of IPv4 and IPv6 (<c>MIB_TCPSTATS.dwAttemptFails</c>) is posted as failures per
        /// second, shown per minute, with an alert when it stays above 1 per second for 5 minutes.
        /// Opt-in: it is not part of <see cref="AddAllNetworkSensors"/>. The native collector has the
        /// same sensor (<c>hsm_collector_enable_tcp_connection_failure_rate_sensor</c>).
        /// </summary>
        IWindowsCollection AddNetworkConnectionFailuresRate(TimeSpan? period = null);

        IWindowsCollection AddAllNetworkSensors(NetworkSensorOptions options = null);


        IWindowsCollection SubscribeToWindowsServiceStatus(string serviceName);

        IWindowsCollection SubscribeToWindowsServiceStatus(ServiceSensorOptions options);

        bool UnsubscribeWindowsServiceStatus(ServiceSensorOptions options);


        /// <summary>
        /// Enable periodic per-process CPU monitoring (#1179). Samples all processes every
        /// <paramref name="period"/> (default 1 minute), posts Double sensors at
        /// "Top CPU processes/&lt;exe-name&gt;" for the <paramref name="count"/> busiest processes
        /// above <paramref name="minPercent"/>% of total machine CPU. Windows only.
        /// The number of distinct per-process sensors is capped (max(<paramref name="count"/> * 8, 64))
        /// so a host that churns through many distinctly named processes cannot grow the sensor
        /// namespace without bound; once the cap is reached, newly seen process names are skipped.
        /// </summary>
        IWindowsCollection AddTopCpuProcessesSensors(int count = 10, double minPercent = 1.0, TimeSpan? period = null);
    }
}