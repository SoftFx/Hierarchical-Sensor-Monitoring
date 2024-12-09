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
        private readonly TimeSpan _faultStateDelay = TimeSpan.FromHours(1);
        private readonly string _serviceName;

        private ServiceController _service;

        private ServiceControllerStatus? _lastServiceState;
        private Task _statusWatcher;
        private CancellationTokenSource _cancellationTokenSource;

        private bool _faultState = true;

        private readonly object _locker = new object();


        internal WindowsServiceStatusSensor(ServiceSensorOptions options) : base(options)
        {
            _serviceName = options.ServiceName;
        }


        internal override ValueTask<bool> StartAsync()
        {
            lock (_locker)
            {
                if (_statusWatcher == null)
                {
                    _cancellationTokenSource = new CancellationTokenSource();
                    _statusWatcher = PeriodicTask.Run(CheckServiceStatusAsync, _scanPeriod, _scanPeriod, _cancellationTokenSource.Token);
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


        private async Task CheckServiceStatusAsync()
        {
            try
            {
                if (_faultState)
                {
                    _service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == _serviceName);
                    if (_service == null)
                    {
                        SendValue(-1, HSMSensorDataObjects.SensorStatus.Error, "Service not found!");
                        _lastServiceState = null;
                        await Task.Delay(_faultStateDelay - _scanPeriod, _cancellationTokenSource.Token);
                        return;
                    }

                    _faultState = false;
                }
                
                if (!_faultState)
                {
                    _service.Refresh();

                    if (_service.Status != _lastServiceState)
                    {
                        SendValue((int)_service.Status);
                        _lastServiceState = _service.Status;
                    }
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
                _faultState = true;
                _lastServiceState = null;
            }
        }

    }
}
