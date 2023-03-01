using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsFreeDiskSpace : FreeDiskSpaceBase
    {
        internal WindowsFreeDiskSpace(DiskSensorOptions options)
            : base(options, new WindowsDiskInfo(options.TargetPath)) { }
    }
}
