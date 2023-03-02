using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsFreeDiskSpacePrediction : FreeDiskSpacePredictionBase
    {
        public WindowsFreeDiskSpacePrediction(DiskSensorOptions options)
            : base(options, new WindowsDiskInfo(options.TargetPath)) { }
    }
}
