using System;
using System.Threading.Tasks;
using HSMDataCollector.DefaultSensors.Windows.Process;
using HSMDataCollector.Options;


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


        internal override ValueTask<bool> InitAsync()
        {
            try
            {
                _listener = new ProcessEventListener();
                _listener.OnTimeInGC += OnTimeInGC;
            }
            catch (Exception ex)
            {
                ThrowException(new Exception($"Error initializing performance counter: {WindowsTimeInGCBase.Category}/{WindowsTimeInGCBase.Counter} instance {ProcessInfo.CurrentProcessName}: {ex}"));

                return new ValueTask<bool>(false);
            }

            return base.InitAsync();
        }

        internal override ValueTask StopAsync()
        {
            _listener.OnTimeInGC -= OnTimeInGC;
            _listener.Dispose();

            return base.StopAsync();
        }

        private void OnTimeInGC(double value) => AddValue(value);

    }
#endif
}
