using HSMDataCollector.DefaultSensors.Windows.Process;
using HSMDataCollector.Options;
using System;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors.Windows
{
#if !NETSTANDARD2_0
    internal sealed class WindowsProcessTimeInGC : WindowsTimeInGCBase
    {
        protected override string InstanceName => ProcessInfo.CurrentProcessName;


        internal WindowsProcessTimeInGC(BarSensorOptions options) : base(options) { }
    }
#else
    internal sealed class WindowsProcessTimeInGC : CollectableBarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        private ProcessEventListener _listener;


        internal WindowsProcessTimeInGC(BarSensorOptions options) : base(options) { }


        internal override Task<bool> Init()
        {
            try
            {
                _listener = new ProcessEventListener();
            }
            catch (Exception ex)
            {
                ThrowException(new Exception($"Error initializing performance counter: {WindowsTimeInGCBase.Category}/{WindowsTimeInGCBase.Counter} instance {ProcessInfo.CurrentProcessName}: {ex}"));

                return Task.FromResult(false);
            }

            return base.Init();
        }

        internal override Task Stop()
        {
            _listener?.Dispose();

            return base.Stop();
        }


        protected override double GetBarData() => _listener.TimeInGC;
    }
#endif
}
