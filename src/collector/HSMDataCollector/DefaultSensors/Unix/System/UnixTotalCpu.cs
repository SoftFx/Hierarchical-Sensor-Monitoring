using HSMDataCollector.Extensions;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixTotalCpu : BarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        private const string TotalCpuBashCommand = "top -bn1 | grep \"Cpu(s)\" | sed \"s/.*, *\\([0-9.]*\\)%* id.*/\\1/\" | awk '{print 100 - $1}'";


        protected override string SensorName => "Total CPU";


        internal UnixTotalCpu(BarSensorOptions options) : base(options) { }


        protected override double GetBarData()
        {
            _needSendValue = double.TryParse(TotalCpuBashCommand.BashExecute().Replace("\n", ""), out var barData);

            return _needSendValue ? barData : -1.0;
        }
    }
}