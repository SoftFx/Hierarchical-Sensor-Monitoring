using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public sealed class WindowsFreeDiskSpace : FreeDiskSpaceBase
    {
        internal WindowsFreeDiskSpace(DiskSensorOptions options) : base(options) { }
    }
}