using HSMDataCollector.Extensions;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixFreeRamMemory : CollectableBarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        private const string TotalCpuBashCommand = "free -m | awk 'NR==2 { print $7 }'";


        internal UnixFreeRamMemory(BarSensorOptions options) : base(options) { }


        protected override double? GetBarData()
        {
            if (double.TryParse(TotalCpuBashCommand.BashExecute().Replace("\n", ""), out var barData))
                return barData;

            return null;
        }
    }
}