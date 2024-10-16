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

        private ServiceControllerStatus? _lastServiceState;
        private Task _statusWatcher;
        private CancellationTokenSource _cancellationTokenSource;

        private readonly object _locker = new object();

        internal WindowsServiceStatusSensor(ServiceSensorOptions options) : base(options)
        {
            try
            {
                _controller = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == options.ServiceName) ?? throw new ArgumentException($"Service {options.ServiceName} not found!");
            }
            catch(Exception ex)
            {
                HandleException(ex);
            }
        }


        internal override ValueTask<bool> StartAsync()
        {
            lock (_locker)
            {
                if (_controller == null)
                {
                    SendValue(0, HSMSensorDataObjects.SensorStatus.Error, $"Service not found");
                }
                else if (_statusWatcher == null)
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
                    taskToWait.Dispose();
                }

                await base.StopAsync();
            }
            catch (OperationCanceledException) { }
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
                    SendValue((int)_controller.Status);
                    _lastServiceState = _controller.Status;
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

    }
}
