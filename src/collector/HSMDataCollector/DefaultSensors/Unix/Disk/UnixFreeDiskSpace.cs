using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixFreeDiskSpace : FreeDiskSpaceBase
    {
        public UnixFreeDiskSpace(MonitoringSensorOptions options)
            : base(options, new UnixDiskInfo()) { }
    }
}
