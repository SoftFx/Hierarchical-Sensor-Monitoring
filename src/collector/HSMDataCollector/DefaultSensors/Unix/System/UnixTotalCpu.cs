using HSMDataCollector.Extensions;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixTotalCpu : CollectableBarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        private const string TotalCpuBashCommand = "top -bn1 | grep \"Cpu(s)\" | sed \"s/.*, *\\([0-9.]*\\)%* id.*/\\1/\" | awk '{print 100 - $1}'";


        internal UnixTotalCpu(BarSensorOptions options) : base(options) { }


        protected override double? GetBarData()
        {
           if(double.TryParse(TotalCpuBashCommand.BashExecute().Replace("\n", ""), out var barData))
                return barData;

            return null;
        }
    }
}