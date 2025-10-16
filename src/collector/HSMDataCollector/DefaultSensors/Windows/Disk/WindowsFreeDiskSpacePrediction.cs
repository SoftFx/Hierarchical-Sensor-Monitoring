using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public sealed class WindowsFreeDiskSpacePrediction : FreeDiskSpacePredictionBase
    {
        internal WindowsFreeDiskSpacePrediction(DiskSensorOptions options)
            : base(options, new WindowsDiskInfo(options.TargetPath)) { }
    }
}
