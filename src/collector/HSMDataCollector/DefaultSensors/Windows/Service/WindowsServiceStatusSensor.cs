using System;
using System.Linq;
using System.ServiceProcess;
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
        private ScheduledTask _statusWatcher;
        private DateTime _nextServiceResolveTime;

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
                    _statusWatcher = CollectorScheduler.Schedule(CheckServiceStatus, _scanPeriod, _scanPeriod, HandleException);
                }
            }

            return base.StartAsync();
        }


        public override async ValueTask StopAsync()
        {
            ScheduledTask taskToWait = null;

            lock (_locker)
            {
                if (_statusWatcher != null)
                {
                    taskToWait = _statusWatcher;
                    _statusWatcher = null;
                }
            }

            try
            {
                if (taskToWait != null)
                {
                    await taskToWait.StopAsync().ConfigureAwait(false);
                }

                await base.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HandleException(ex);
                throw;
            }
        }


        private void CheckServiceStatus()
        {
            try
            {
                if (_faultState)
                {
                    if (_nextServiceResolveTime > DateTime.UtcNow)
                        return;

                    _service = ServiceController.GetServices()
                                                .FirstOrDefault(s => string.Equals(s.ServiceName, _serviceName, StringComparison.OrdinalIgnoreCase));

                    if (_service is null)
                    {
                        SendValue(-1, HSMSensorDataObjects.SensorStatus.Error, "Service not found!");
                        _lastServiceState = null;
                        _nextServiceResolveTime = DateTime.UtcNow + _faultStateDelay;
                        return;
                    }

                    _faultState = false;
                    _nextServiceResolveTime = DateTime.MinValue;
                }

                _service.Refresh();

                if (_service.Status != _lastServiceState)
                {
                    SendValue((int)_service.Status);
                    _lastServiceState = _service.Status;
                }
            }
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
