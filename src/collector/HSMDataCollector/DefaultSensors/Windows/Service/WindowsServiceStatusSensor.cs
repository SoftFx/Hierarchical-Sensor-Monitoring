using System;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Options;
using HSMDataCollector.Threading;


namespace HSMDataCollector.DefaultSensors.Windows.Service
{
    internal sealed class WindowsServiceStatusSensor : SensorBase<int>
    {
        private readonly TimeSpan _scanPeriod = TimeSpan.FromSeconds(5);
        private readonly ServiceController _controller;

        private ServiceControllerStatus _lastServiceState;
        private Task _statusWatcher;
        private CancellationTokenSource _cancellationTokenSource;

        internal WindowsServiceStatusSensor(ServiceSensorOptions options) : base(options)
        {
            _controller = GetService(options.ServiceName);
            _lastServiceState = _controller.Status;
        }


        internal override ValueTask<bool> StartAsync()
        {
            if (_statusWatcher == null)
            {
                _cancellationTokenSource = new CancellationTokenSource();
                _statusWatcher = PeriodicTask.Run(CheckServiceStatus, _scanPeriod, _scanPeriod, _cancellationTokenSource.Token);
            }

            return base.StartAsync();
        }

        internal override async ValueTask StopAsync()
        {
            if (_statusWatcher != null)
            {
                _cancellationTokenSource?.Cancel();
                await _statusWatcher.ConfigureAwait(false);
                _cancellationTokenSource?.Dispose();
                _statusWatcher?.Dispose();
                _statusWatcher = null;
            }
            base.StopAsync();
        }


        private void CheckServiceStatus()
        {
            try
            {
                _controller.Refresh();

                if (_controller.Status != _lastServiceState)
                {
                    _lastServiceState = _controller.Status;
                    SendValue((int)_lastServiceState);
                }
            }
            catch (Exception ex)
            {
                ThrowException(ex);
            }
        }

        private static ServiceController GetService(string serviceName) =>
            ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == serviceName) ??
            throw new ArgumentException($"Service {serviceName} not found!");
    }
}
