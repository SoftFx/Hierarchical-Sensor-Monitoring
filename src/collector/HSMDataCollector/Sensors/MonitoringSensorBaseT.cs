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

        // Composed scheduling lifecycle for the periodic send loop (replaces a hand-rolled
        // ScheduledTask field + lock). The bar sensor composes a second handle for its collect loop.
        private readonly ScheduledTaskHandle _sendHandle;
        private int _sendValueInProgress;
        private int _stopping;

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

            _sendHandle = new ScheduledTaskHandle(_dataProcessor.Scheduler);
        }

        public override ValueTask<bool> InitAsync()
        {
            try
            {
                Volatile.Write(ref _stopping, 0);
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
                Volatile.Write(ref _stopping, 1);

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
            if (Volatile.Read(ref _stopping) == 1)
                return;

            if (Interlocked.Exchange(ref _sendValueInProgress, 1) == 1)
                return;

            try
            {
                var value = BuildSensorValue();

                if (Volatile.Read(ref _stopping) == 1)
                    return;

                SendValue(value);
            }
            finally
            {
                Volatile.Write(ref _sendValueInProgress, 0);
            }
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

        private ValueTask StopInternalAsync(bool waitForCurrentRun) => _sendHandle.StopAsync(waitForCurrentRun);

        private void StartSendTask() => _sendHandle.Start(SendValueAction, TimerDueTime, PostTimePeriod, HandleException);

        private SensorValueBase BuildSensorValue()
        {
            try
            {
                T value = GetValue();
                if (value == null)
                    return default;

                var status = GetStatus();
                if (!SensorValueExtensions.IsValidValue(value, status))
                    return default;

                return GetSensorValue(value).Complete(GetComment(), status);
            }
            catch (Exception ex)
            {
                HandleException(ex);

                return GetSensorValue(GetDefaultValue()).Complete(ex.Message, SensorStatus.Error);
            }
        }
    }
}
