using HSMDataCollector.Extensions;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixFreeRamMemory : BarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        private const string TotalCpuBashCommand = "free -m | awk 'NR==2 { print $7 }'";


        protected override string SensorName => "Free RAM memory MB";


        internal UnixFreeRamMemory(BarSensorOptions options) : base(options) { }


        protected override double GetBarData()
        {
            _needSendValue = double.TryParse(TotalCpuBashCommand.BashExecute().Replace("\n", ""), out var barData);

            return _needSendValue ? barData : -1.0;
        }
    }
}