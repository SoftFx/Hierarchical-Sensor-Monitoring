using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.Threading;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase<T> : SensorBase<T>
    {
        private readonly IMonitoringOptions _options;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _sendTask;

        private readonly object _lock = new object();

        protected virtual TimeSpan TimerDueTime => PostTimePeriod;

        protected TimeSpan PostTimePeriod => _options.PostDataPeriod;

        protected MonitoringSensorBase(SensorOptions options) : base(options)
        {
            if (options is IMonitoringOptions monitoringOptions)
                _options = monitoringOptions;
            else
                throw new ArgumentNullException(nameof(monitoringOptions));
        }

        internal override ValueTask<bool> InitAsync()
        {
            try
            {
                StartSendTask();

                return base.InitAsync();
            }
            catch (Exception ex)
            {
                HandleException(ex);
                return new ValueTask<bool>(false);
            }
        }

        internal override async ValueTask StopAsync()
        {
            try
            {
                await StopInternalAsync();

                await base.StopAsync();
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        protected abstract T GetValue();

        protected virtual string GetComment() => null;

        protected virtual T GetDefaultValue() => default;

        protected virtual SensorStatus GetStatus() => SensorStatus.Ok;

        protected void SendValueAction()
        {
            SendValue(BuildSensorValue());
        }

        protected async Task RestartTimerAsync(TimeSpan newPostPeriod)
        {
            try
            {
                await StopInternalAsync().ConfigureAwait(false);

                _options.PostDataPeriod = newPostPeriod;

                StartSendTask();
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        private async ValueTask StopInternalAsync()
        {
            Task taskToAwait = null;

            lock (_lock)
            {
                if (_sendTask != null)
                {
                    _cancellationTokenSource?.Cancel();
                    taskToAwait = _sendTask;
                    _sendTask = null;
                    _cancellationTokenSource?.Dispose();
                }
            }

            if (taskToAwait != null)
            {
                await taskToAwait.ConfigureAwait(false);
                taskToAwait.Dispose();
            }
        }

        private void StartSendTask()
        {
            lock (_lock)
            {
                if (_sendTask == null)
                {
                    _cancellationTokenSource = new CancellationTokenSource();
                    _sendTask = PeriodicTask.Run(SendValueAction, TimerDueTime, PostTimePeriod, _cancellationTokenSource.Token);
                }
            }
        }

        private SensorValueBase BuildSensorValue()
        {
            try
            {
                T value = GetValue();
                if (value == null)
                    return default;

                return GetSensorValue(value).Complete(GetComment(), GetStatus());
            }
            catch (Exception ex)
            {
                HandleException(ex);

                return GetSensorValue(GetDefaultValue()).Complete(ex.Message, SensorStatus.Error);
            }
        }
    }
}
