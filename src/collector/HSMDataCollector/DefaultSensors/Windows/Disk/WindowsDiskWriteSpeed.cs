using HSMDataCollector.Extensions;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public sealed class WindowsDiskWriteSpeed : WindowsDiskBarSensorBase
    {
        protected override string CounterName => "Disk Write Bytes/sec";


        internal WindowsDiskWriteSpeed(DiskBarSensorOptions options) : base(options) { }


        protected override double? GetBarData() => base.GetBarData()?.BytesToMegabytesDouble();
    }
}