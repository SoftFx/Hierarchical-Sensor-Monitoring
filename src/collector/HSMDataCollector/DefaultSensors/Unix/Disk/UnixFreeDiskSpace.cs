using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixFreeDiskSpace : FreeDiskSpaceBase
    {
        public UnixFreeDiskSpace(DiskSensorOptions options) : base(options) { }
    }
}