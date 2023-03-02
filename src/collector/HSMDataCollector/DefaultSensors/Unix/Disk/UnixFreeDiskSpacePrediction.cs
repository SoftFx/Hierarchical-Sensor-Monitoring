using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixFreeDiskSpacePrediction : FreeDiskSpacePredictionBase
    {
        public UnixFreeDiskSpacePrediction(DiskSensorOptions options)
            : base(options, new UnixDiskInfo()) { }
    }
}
