using HSMDataCollector.DefaultSensors.Windows.Process;
using HSMDataCollector.Options;
using System;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors.Windows
{
#if !NET6_0_OR_GREATER
    internal sealed class WindowsProcessTimeInGC : WindowsTimeInGCBase
    {
        protected override string InstanceName => ProcessInfo.CurrentProcessName;


        internal WindowsProcessTimeInGC(BarSensorOptions options) : base(options) { }
    }
#else
    internal sealed class WindowsProcessTimeInGC : DoubleBarPublicSensor
    {
        private ProcessEventListener _listener;


        internal WindowsProcessTimeInGC(BarSensorOptions options) : base(options) { }


        internal override Task<bool> Init()
        {
            try
            {
                _listener = new ProcessEventListener();
                _listener.OnTimeInGC += OnTimeInGC;
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
            _listener.OnTimeInGC -= OnTimeInGC;
            _listener.Dispose();

            return base.Stop();
        }

        private void OnTimeInGC(double value) => AddValue(value);

    }
#endif
}
