using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Unix
{
    public sealed class UnixFreeDiskSpace : FreeDiskSpaceBase
    {
        public UnixFreeDiskSpace(DiskSensorOptions options) : base(options) { }
    }
}