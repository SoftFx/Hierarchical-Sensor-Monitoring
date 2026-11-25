using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.Threading;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.DefaultSensors
{
    public abstract class BarMonitoringSensorBase<BarType, T> : MonitoringSensorBase<BarType, NoDisplayUnit>
        where BarType : MonitoringBarBase<T>, new()
        where T : struct
    {
        private readonly object _lockBar = new object();

        private readonly TimeSpan _collectBarPeriod;
        private readonly TimeSpan _barPeriod;
        private readonly int _precision;

        // Composed scheduling lifecycle for the bar-collect loop (the send loop is owned by the
        // MonitoringSensorBase base via its own handle). Replaces a hand-rolled ScheduledTask + lock.
        private readonly ScheduledTaskHandle _collectHandle;

        protected BarType _internalBar;

        public override BarType Current => (BarType)_internalBar.Copy().Complete();

        protected sealed override TimeSpan TimerDueTime => BarTimeHelper.GetTimerDueTime(PostTimePeriod);


        protected BarMonitoringSensorBase(BarSensorOptions options) : base(options)
        {
            if (options.BarTickPeriod <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options.BarTickPeriod), "Bar tick period must be greater than zero.");

            if (options.BarPeriod <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options.BarPeriod), "Bar period must be greater than zero.");

            if (typeof(BarType) == typeof(DoubleMonitoringBar) && (options.Precision < 0 || options.Precision > 15))
                throw new ArgumentOutOfRangeException(nameof(options.Precision), "Precision must be between 0 and 15.");

            _collectBarPeriod = options.BarTickPeriod;
            _barPeriod = options.BarPeriod;
            _precision = options.Precision;

            _collectHandle = new ScheduledTaskHandle(_dataProcessor.Scheduler);

            BuildNewBar();
        }


        public override ValueTask<bool> InitAsync()
        {
            lock (_lockBar)
                _lastBarTaken = false;

            _collectHandle.Start(CollectBar, _collectBarPeriod, _collectBarPeriod, HandleException);

            return base.InitAsync();
        }

        public override ValueTask StopAsync() => StopCoreAsync(flushPartialBar: true);

        // Sensor disposal (without a collector Stop) must not flush — mirrors
        // LastValueSensorInstant.DisposeAsyncCore: releasing a handle is not a data point.
        protected override ValueTask DisposeAsyncCore() => StopCoreAsync(flushPartialBar: false);

        private async ValueTask StopCoreAsync(bool flushPartialBar)
        {
            try
            {
                await _collectHandle.StopAsync(waitForCurrentRun: true).ConfigureAwait(false);

                // From here no value of this run reaches a bar that will be published: TryWhileBarOpen says so to a
                // caller that would otherwise lose what it adds (the table loss ledger).
                lock (_lockBar)
                    _lastBarTaken = true;

                if (flushPartialBar)
                {
                    // A periodic send in flight goes first (#1846 review). It may have taken its snapshot
                    // already: a value added after that snapshot is in the live bar only, and the final send
                    // below, finding the send guard held, used to skip and leave that value in no published
                    // bar. The send loop is stopped here, with the same bounded wait base.StopAsync would
                    // spend on that send; with no send in flight this does nothing, and the final send is
                    // what it always was.
                    await StopSendLoopAsync().ConfigureAwait(false);

                    // Flush the in-progress partial bar before base.StopAsync bumps the lifecycle
                    // epoch and the data queues drain — otherwise everything accumulated since the
                    // last CloseTime is lost at shutdown. Same roll-on-successful-send contract as
                    // CheckCurrentBar: if a periodic send outlived the bounded wait above (TrySendValue
                    // returns false), that send publishes its snapshot itself and the bar intentionally
                    // stays un-rolled (the server merges partial bars by OpenTime).
                    lock (_lockBar)
                    {
                        if (_internalBar.Count > 0 && TrySendValue())
                            BuildNewBar();
                    }
                }

                await base.StopAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }


        // True from the stop's last bar (or a dispose) until the next InitAsync.
        private bool _lastBarTaken;

        /// <summary>
        /// Runs <paramref name="add"/> under the bar's lock unless the sensor has taken its last bar of the run; false
        /// then, and <paramref name="add"/> does not run. A value added here is in a bar that will be published.
        /// </summary>
        internal bool TryWhileBarOpen(Action add)
        {
            lock (_lockBar)
            {
                if (_lastBarTaken)
                    return false;

                add();
                return true;
            }
        }

        protected virtual void CollectBar() => CheckCurrentBar();

        protected sealed override BarType GetValue()
        {
            lock (_lockBar)
            {
                if (_internalBar.Count <= 0)
                    return null;

                return _internalBar.Copy().Complete() as BarType; //need copy for correct partialBar serialization
            }
        }

        protected sealed override BarType GetDefaultValue() =>
            new BarType()
            {
                OpenTime  = _internalBar?.OpenTime ?? DateTime.UtcNow,
                CloseTime = _internalBar?.CloseTime ?? DateTime.UtcNow,
                Count = 1,
            };

        protected void CheckCurrentBar()
        {
            try
            {
                // Capture the generation once: if the sensor stops or restarts between this entry
                // and the lock acquisition, we drop the bar instead of sending an obsolete one.
                var capturedEpoch = LifecycleEpoch;

                lock (_lockBar)
                {
                    if (LifecycleEpoch != capturedEpoch)
                        return;

                    if (_internalBar.CloseTime < DateTime.UtcNow)
                    {
                        // Roll the bar ONLY if we actually sent its snapshot. The periodic send
                        // schedule shares _sendValueInProgress with us, and may already be in
                        // SendValueAction at this moment without having taken _lockBar yet to
                        // snapshot. If we blindly BuildNewBar after a guard-skipped no-op, the
                        // periodic send's GetValue lands on the freshly-reset (empty) bar and
                        // the closed bar's aggregated data is lost. Deferring the roll keeps the
                        // bar intact: either the periodic send finishes its snapshot (data sent
                        // by the other thread), or our next CheckCurrentBar tick rolls cleanly
                        // once the guard releases.
                        if (TrySendValue())
                            BuildNewBar();
                    }
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }


        protected virtual void BuildNewBar()
        {
            _internalBar = new BarType();
            _internalBar.Init(_barPeriod, _precision);
        }
    }
}
