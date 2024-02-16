using HSMDataCollector.Extensions;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal class WindowsAverageDiskWriteSpeed : WindowsDiskBarSensorBase
    {
        public const string Counter = "Disk Write MBytes/sec";


        protected override string CounterName => Counter;


        public WindowsAverageDiskWriteSpeed(DiskBarSensorOptions options) : base(options) { }
        
        
        protected override double GetBarData() => base.GetBarData().BytesToMegabytesDouble();
    }
}