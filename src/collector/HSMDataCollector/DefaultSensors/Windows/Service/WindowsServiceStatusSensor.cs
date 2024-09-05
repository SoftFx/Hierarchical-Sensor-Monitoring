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

        private readonly object _locker = new object();

        internal WindowsServiceStatusSensor(ServiceSensorOptions options) : base(options)
        {
            _controller = GetService(options.ServiceName);
            _lastServiceState = _controller.Status;
        }


        internal override ValueTask<bool> StartAsync()
        {
            lock (_locker)
            {
                if (_statusWatcher == null)
                {
                    _cancellationTokenSource = new CancellationTokenSource();
                    _statusWatcher = PeriodicTask.Run(CheckServiceStatus, _scanPeriod, _scanPeriod, _cancellationTokenSource.Token);
                }
            }

            return base.StartAsync();
        }

        internal override async ValueTask StopAsync()
        {
            try
            {
                Task taskToWait = null;

                lock (_locker)
                {
                    if (_statusWatcher != null)
                    {
                        _cancellationTokenSource?.Cancel();
                        taskToWait = _statusWatcher;
                        _cancellationTokenSource?.Dispose();
                        _statusWatcher = null;
                    }
                }

                if (taskToWait != null)
                {
                    await taskToWait.ConfigureAwait(false);
                    taskToWait?.Dispose();
                }

                await base.StopAsync();
            }
            catch (Exception ex)
            { 
                HandleException(ex);
            }
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
                HandleException(ex);
            }
        }

        private static ServiceController GetService(string serviceName) =>
            ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == serviceName) ??
            throw new ArgumentException($"Service {serviceName} not found!");
    }
}
