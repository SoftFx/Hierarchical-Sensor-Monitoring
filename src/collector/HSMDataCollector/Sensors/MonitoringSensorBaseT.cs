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
            if (_sendTask == null)
                StartSendTask();

            return base.InitAsync();
        }

        internal override async ValueTask StopAsync()
        {
            if (_sendTask != null)
                await StopInternalAsync();

            await base.StopAsync();
        }

        protected abstract T GetValue();

        protected virtual string GetComment() => null;

        protected virtual T GetDefaultValue() => default;

        protected virtual SensorStatus GetStatus() => SensorStatus.Ok;

        protected void SendValueAction()
        {
            SendValue(BuildSensorValue());
        }

        protected void RestartTimer(TimeSpan newPostPeriod)
        {
            if (_sendTask != null)
                StopInternalAsync().ConfigureAwait(false).GetAwaiter().GetResult();

            _options.PostDataPeriod = newPostPeriod;

            StartSendTask();
        }

        private async ValueTask StopInternalAsync()
        {
            _cancellationTokenSource?.Cancel();
            await _sendTask.ConfigureAwait(false);
            _sendTask?.Dispose();
            _sendTask = null;
            _cancellationTokenSource?.Dispose();
        }

        private void StartSendTask()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _sendTask = PeriodicTask.Run(SendValueAction, TimerDueTime, PostTimePeriod, _cancellationTokenSource.Token);
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
                ThrowException(ex);

                return GetSensorValue(GetDefaultValue()).Complete(ex.Message, SensorStatus.Error);
            }
        }
    }
}
