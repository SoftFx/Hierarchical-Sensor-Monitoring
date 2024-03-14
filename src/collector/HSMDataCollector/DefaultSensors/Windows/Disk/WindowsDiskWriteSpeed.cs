using HSMDataCollector.Extensions;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal class WindowsDiskWriteSpeed : WindowsDiskBarSensorBase
    {
        protected override string CounterName => "Disk Write Bytes/sec";


        public WindowsDiskWriteSpeed(DiskBarSensorOptions options) : base(options) { }
        
        
        protected override double GetBarData() => base.GetBarData().BytesToMegabytesDouble();
    }
}