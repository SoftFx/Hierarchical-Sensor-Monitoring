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
    public abstract class MonitoringSensorBase<T, TDisplayUnit> : SensorBase<T, TDisplayUnit> where TDisplayUnit : struct, Enum
    {
        private readonly IMonitoringOptions _options;
        private ScheduledTask _sendTask;

        private readonly object _lock = new object();

        protected virtual TimeSpan TimerDueTime => TimeSpan.Zero;

        protected TimeSpan PostTimePeriod => _options.PostDataPeriod;

        protected MonitoringSensorBase(SensorOptions<TDisplayUnit> options) : base(options)
        {
            if (options is IMonitoringOptions monitoringOptions)
                _options = monitoringOptions;
            else
                throw new ArgumentNullException(nameof(monitoringOptions));

            if (_options.PostDataPeriod <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(_options.PostDataPeriod), "Post data period must be greater than zero.");
        }

        public override ValueTask<bool> InitAsync()
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

        public override async ValueTask StopAsync()
        {
            try
            {
                await StopInternalAsync(waitForCurrentRun: true);

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
                await StopInternalAsync(waitForCurrentRun: true).ConfigureAwait(false);

                if (newPostPeriod <= TimeSpan.Zero)
                    throw new ArgumentOutOfRangeException(nameof(newPostPeriod), "Post data period must be greater than zero.");

                _options.PostDataPeriod = newPostPeriod;

                StartSendTask();
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        private async ValueTask StopInternalAsync(bool waitForCurrentRun)
        {
            ScheduledTask taskToAwait = null;

            lock (_lock)
            {
                if (_sendTask != null)
                {
                    taskToAwait = _sendTask;
                    _sendTask = null;
                }
            }

            if (taskToAwait != null)
            {
                await taskToAwait.StopAsync(waitForCurrentRun).ConfigureAwait(false);
            }
        }

        private void StartSendTask()
        {
            lock (_lock)
            {
                if (_sendTask == null)
                {
                    _sendTask = _dataProcessor.Scheduler.Schedule(SendValueAction, TimerDueTime, PostTimePeriod, HandleException);
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
