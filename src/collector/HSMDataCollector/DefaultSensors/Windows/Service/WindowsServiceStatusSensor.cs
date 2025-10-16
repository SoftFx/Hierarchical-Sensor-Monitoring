using System;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Options;
using HSMDataCollector.Threading;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.DefaultSensors.Windows.Service
{
    internal sealed class WindowsServiceStatusSensor : SensorBase<int, NoDisplayUnit>
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


        public override ValueTask<bool> StartAsync()
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


        public override async ValueTask StopAsync()
        {
            Task taskToWait = null;
            CancellationTokenSource cts = null;

            lock (_locker)
            {
                if (_statusWatcher != null)
                {
                    cts = _cancellationTokenSource;
                    _cancellationTokenSource = null;

                    taskToWait = _statusWatcher;
                    _statusWatcher = null;

                    cts?.Cancel();
                }
            }

            try
            {
                if (taskToWait != null)
                {
                    try
                    {
                        await taskToWait.ConfigureAwait(false);
                    }
                    finally
                    {
                        taskToWait.Dispose();
                    }
                }

                await base.StopAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                HandleException(ex);
                throw;
            }
            finally
            {
                cts?.Dispose();
            }
        }


        private async Task CheckServiceStatusAsync()
        {
            try
            {
                if (_faultState)
                {
                    _service = ServiceController.GetServices()
                                                .FirstOrDefault(s => string.Equals(s.ServiceName, _serviceName, StringComparison.OrdinalIgnoreCase));

                    if (_service is null)
                    {
                        SendValue(-1, HSMSensorDataObjects.SensorStatus.Error, "Service not found!");
                        _lastServiceState = null;

                        var delay = _faultStateDelay - _scanPeriod;
                        if (delay > TimeSpan.Zero)
                        {
                            await Task.Delay(delay, _cancellationTokenSource.Token);
                        }
                        return;
                    }

                    _faultState = false;
                }

                _service.Refresh();

                if (_service.Status != _lastServiceState)
                {
                    SendValue((int)_service.Status);
                    _lastServiceState = _service.Status;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                HandleException(ex);
                _faultState = true;
                _lastServiceState = null;

                _service?.Dispose();
                _service = null;
            }
        }

    }
}
