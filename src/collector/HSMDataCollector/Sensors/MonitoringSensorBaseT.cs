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

        // Lifecycle generation token (issue #1074). Incremented every time the send loop is started,
        // restarted, or stopped. Scheduled callbacks capture the value at entry and verify the
        // sensor is still on the same generation before publishing a value. This kills two classes
        // of races: a callback overlapping a restart boundary, and a long-running callback that
        // outlives the bounded sensor-stop wait and tries to publish into a queue that is already
        // past its final drain. Writes go through Interlocked.Increment; reads use
        // Interlocked.Read because Volatile.Read of a long is not atomic on 32-bit runtimes —
        // a torn read of the epoch would defeat the whole guard.
        private long _lifecycleEpoch;

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
                Interlocked.Increment(ref _lifecycleEpoch);
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
                // Order matters. We await the bounded wait FIRST so a short in-flight callback
                // gets to finish, build its value, and publish on its original generation — that
                // value will then be flushed by DataProcessor.StopAsync. Only AFTER the bounded
                // wait do we bump the epoch, so a callback that outlived the bound observes a
                // stale generation when it eventually wakes and skips its SendValue rather than
                // landing in a queue that is about to be drained.
                await StopInternalAsync(waitForCurrentRun: true);

                Interlocked.Increment(ref _lifecycleEpoch);

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

        // Scheduler-facing entry point (must be `void` to bind as Action). Delegates to
        // <see cref="TrySendValue"/> and discards the result.
        protected void SendValueAction() => TrySendValue();

        /// <summary>
        /// Build and publish the current sensor value, gated by the <c>_sendValueInProgress</c>
        /// reentrancy guard and the lifecycle epoch. Returns <c>true</c> iff a value was actually
        /// sent (or the guard's owner is expected to send shortly — see remarks). Callers that
        /// roll the sensor state on send (notably <see cref="BarMonitoringSensorBase.CheckCurrentBar"/>,
        /// which calls <see cref="BarMonitoringSensorBase.BuildNewBar"/> after a successful send)
        /// MUST condition that roll on this return value — otherwise a concurrent in-flight send
        /// from the periodic schedule that hasn't yet snapshotted state can race the roll and
        /// observe an already-reset bar, dropping the closed bar's data.
        /// </summary>
        protected bool TrySendValue()
        {
            if (Interlocked.Exchange(ref _sendValueInProgress, 1) == 1)
                return false;

            // Capture the generation when the callback starts. If it changes before SendValue
            // runs, the sensor has restarted or stopped underneath us — drop the value rather
            // than publish stale work into a queue that may have already been drained.
            try
            {
                var capturedEpoch = Interlocked.Read(ref _lifecycleEpoch);

                var value = BuildSensorValue();

                if (Interlocked.Read(ref _lifecycleEpoch) != capturedEpoch)
                    return false;

                SendValue(value);
                return true;
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

                // Bump after the bounded wait but BEFORE rebuilding the schedule. Any callback
                // from the old period that outlived the wait sees a stale generation and skips
                // its SendValue, even if it wakes after the new schedule has already started.
                Interlocked.Increment(ref _lifecycleEpoch);

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

        /// <summary>
        /// Current lifecycle generation for derived sensors (e.g. bar collect loops) that need
        /// to honour the same invalidation boundary as the send loop.
        /// </summary>
        protected long LifecycleEpoch => Interlocked.Read(ref _lifecycleEpoch);

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
                {
                    _dataProcessor.LogDroppedValue(SensorPath, $"monitoring sample failed validation (status: {status})");
                    return default;
                }

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
