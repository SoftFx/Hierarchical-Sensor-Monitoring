using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.Threading;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.DefaultSensors
{
    /// <summary>
    /// "Free space on disk prediction" — how long the free space lasts at the recently observed
    /// drain rate.
    ///
    /// The drain rate is a signed exponential moving average over EVERY sampling interval (#1445):
    /// a positive sample means space was consumed, a negative one means it was freed, and a flat
    /// interval folds in a zero. Because every interval is folded, a burst of writes decays out of
    /// the estimate once it stops instead of pinning a high drain rate forever, which is what made
    /// the sensor predict a full disk on an idle host.
    ///
    /// Free space is measured every ten minutes and the average spans six hours, because that is the
    /// scale the answer lives on. A minute-scale window made the sensor report a four-fold swing in
    /// the drain rate between adjacent posts on a host whose free space was actually GROWING, while
    /// the true hour-scale picture on that host was a steady 1.3 GB/h.
    ///
    /// The value posted is a TimeSpan, but only the <see cref="PredictionState.Draining"/> state
    /// carries a real estimate (status Ok). Every other state posts <see cref="MaxPrediction"/>
    /// with status OffTime and a comment that names the state, so a reader can tell "nothing is
    /// draining" from a genuine estimate. TimeSpan.Zero is therefore reserved for its literal
    /// meaning — no free space left at all — and is never used as a placeholder. The one other way
    /// a zero can be posted is a FAILED disk read, which the base class publishes as the default
    /// value with status Error and the failure message as the comment; that is the generic
    /// value-sensor contract, mirrored by the native collector, and it is loud rather than silent.
    /// </summary>
    public abstract class FreeDiskSpacePredictionBase : MonitoringSensorBase<TimeSpan, NoDisplayUnit>
    {
        /// <summary>
        /// How often free space is measured. Ten minutes, not seconds: this sensor answers an
        /// hours-to-days question, and a minute-scale cadence only sampled write bursts (#1445).
        /// </summary>
        public const int DefaultSpaceCheckPeriodInSec = 600;

        /// <summary>
        /// Weight of the newest sampling interval in the drain-speed EMA, chosen together with
        /// <see cref="DefaultSpaceCheckPeriodInSec"/> so the estimate describes the last SIX HOURS
        /// of disk activity (#1445).
        ///
        /// The weights are a geometric series, so the mean age of the samples behind the estimate is
        /// <c>period * (1 - a) / a</c>. With a 10-minute period and <c>a = 1/37</c> that is exactly
        /// 36 periods = 6.0 h; the exponential time constant is <c>period / a</c> = 6.17 h and the
        /// weight of any one sample halves every 4.22 h.
        ///
        /// The point of a window this long is that the sensor answers an hours-to-days question.
        /// A single interval can move the estimate by at most <c>a</c> = 2.7 % of the distance to
        /// what it just measured, so a burst of writes no longer swings the prediction, while a
        /// genuine steady drain is reported exactly (the EMA of identical samples is that sample).
        /// </summary>
        internal const double SpeedSmoothingFactor = 1.0 / 37.0;

        /// <summary>
        /// The ceiling for a posted prediction. Anything at or beyond it is reported as exactly this
        /// value with status OffTime, which an operator reads as "at the current rate this disk will
        /// not fill within a year" — no estimate that far out is actionable, and clamping keeps
        /// <see cref="TimeSpan.FromSeconds(double)"/> from overflowing on a very small drain rate.
        /// </summary>
        internal static readonly TimeSpan MaxPrediction = TimeSpan.FromDays(365);

        private static readonly double _maxPredictionSeconds = MaxPrediction.TotalSeconds;

        private readonly TimeSpan _calculateSpeedDelay;
        private readonly IDiskInfo _diskInfo;
        private readonly int _calibrationRequests;

        private DateTime _lastSpeedCheckTime;
        private bool _hasBaseline;

        private double _currentChangeSpeed;
        private long _lastAvailableSpace;
        private long _samplesCount;

        // The state the current post describes. Written by GetValue and read by GetStatus/GetComment,
        // which the base class always calls in that order on the same thread, so all three fields of
        // one post describe the same instant.
        private PredictionState _state = PredictionState.Calibration;
        private double _postedSpeed;
        private long _postedSamples;

        // Composed scheduling lifecycle for the disk-speed sampling loop (replaces a hand-rolled
        // ScheduledTask + lock). The base MonitoringSensorBase owns its own send-loop handle.
        private readonly ScheduledTaskHandle _workHandle;

        private long FreeSpace => _diskInfo.FreeSpace;

        private readonly object _locker = new object();

        /// <summary>
        /// The sampling clock. Production reads the system clock; the unit tests replace it so the
        /// folding math can be asserted against exact intervals instead of wall-clock timing (#1445).
        /// </summary>
        internal Func<DateTime> UtcNowProvider { get; set; } = () => DateTime.UtcNow;

        internal FreeDiskSpacePredictionBase(DiskSensorOptions options, IDiskInfo diskInfo) : base(options)
        {
            _calculateSpeedDelay = options.SpaceCheckPeriod > TimeSpan.Zero
                ? options.SpaceCheckPeriod
                : TimeSpan.FromSeconds(DefaultSpaceCheckPeriodInSec);
            _calibrationRequests = options.CalibrationRequests;
            _diskInfo = diskInfo;

            _workHandle = new ScheduledTaskHandle(_dataProcessor.Scheduler);
        }


        public override ValueTask<bool> StartAsync()
        {
            // Guard the per-start state reset so a repeated StartAsync does not re-zero the running
            // sampler. _locker still serializes the reset; the handle owns the schedule lifecycle.
            lock (_locker)
            {
                if (!_workHandle.IsScheduled)
                {
                    var utc = UtcNowProvider();

                    // A FAILED initial read must not become a baseline. It used to be harmless — the
                    // huge negative first sample it produced was discarded, because only a positive
                    // speed was folded in — but a signed EMA would SEED on it and spend an hour
                    // decaying back (#1445). The next sampling tick establishes the baseline instead,
                    // which is what the native mirror's has_last_space_ does.
                    _hasBaseline = TryReadFreeSpace(out var freeSpace);
                    _lastSpeedCheckTime = utc;
                    _lastAvailableSpace = freeSpace;

                    Interlocked.Exchange(ref _currentChangeSpeed, 0.0);
                    Interlocked.Exchange(ref _samplesCount, 0L);
                    _state = PredictionState.Calibration;

                    _workHandle.Start(
                        UpdateDiskSpeed, FirstSampleDelay(_calculateSpeedDelay), _calculateSpeedDelay, HandleException);
                }
            }

            return base.StartAsync();
        }

        public override async ValueTask StopAsync()
        {
            try
            {
                await _workHandle.StopAsync(waitForCurrentRun: true).ConfigureAwait(false);

                await base.StopAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }


        protected sealed override string GetComment()
        {
            var mbPerSec = _postedSpeed.BytesToMegabytesDouble();

            switch (_state)
            {
                case PredictionState.Calibration:
                    return $"Calibration request ({_postedSamples}/{_calibrationRequests}). Value cannot be calculated yet.";

                case PredictionState.Growing:
                    return $"Free space increases by {FormatRatePerHour(-mbPerSec)} Mbytes/hour. Value cannot be calculated.";

                case PredictionState.NoDrain:
                    return "Free space is not decreasing. Value cannot be calculated.";

                case PredictionState.BeyondCeiling:
                    return $"Free space decreases by {FormatRatePerHour(mbPerSec)} Mbytes/hour. More than 365 days left.";

                default:
                    return $"Free space decreases by {FormatRatePerHour(mbPerSec)} Mbytes/hour.";
            }
        }


        /// <summary>
        /// The operator-facing rate, in MEGABYTES PER HOUR with up to six decimals (#1460).
        /// Rendering MB/sec through the round-trip form printed a realistic idle drain as
        /// "1.6574101944286661E-06" — 17 digits of scientific notation in a sentence an operator
        /// reads. Per hour is the scale this sensor answers on anyway.
        /// <para>
        /// Six decimals, not three, because the "Mbytes" label is only accurate on Windows: the
        /// comment divides by 1 MiB whatever unit the platform's <see cref="IDiskInfo"/> reports
        /// free space in, and the Unix reader reports kB, so a Unix number is 1024x smaller than
        /// its label says. Three decimals turned an ordinary Unix drain back into "0.000" — the
        /// same structurally-zero reading this issue is about. Trailing zeros are trimmed (one
        /// decimal is always kept), so a fast drain still reads "1800.0".
        /// </para>
        /// <para>
        /// The digits come from INTEGER arithmetic on purpose: the native collector must emit
        /// byte-identical text (repo rule #10), and a ToString("F3")/printf pair does not guarantee
        /// that at a decimal midpoint (round-half-away-from-zero vs round-half-even). Scaling the
        /// same IEEE double by 1000 and rounding half away from zero is defined identically on both
        /// sides — native does std::llround. An absurd magnitude that would overflow the scaled
        /// integer falls back to the invariant round-trip form, exactly as native does.
        /// </para>
        /// </summary>
        private static string FormatRatePerHour(double mbPerSec)
        {
            var perHour = mbPerSec * 3600.0;
            var scaled = perHour * 1000000.0;

            // Named explicitly so the native mirror can match: its round-trip formatter parses an
            // exponent out of the digits and has none to parse for a non-finite value.
            if (double.IsNaN(perHour))
                return "NaN";

            if (double.IsInfinity(perHour))
                return perHour > 0.0 ? "Infinity" : "-Infinity";

            // "R" (round-trip), not the default format: on net472 the default renders 15
            // significant digits while native renders the shortest round-trip form.
            if (double.IsInfinity(scaled) || Math.Abs(scaled) >= 9.0e15)
                return perHour.ToString("R", CultureInfo.InvariantCulture);

            var units = (long)Math.Round(scaled, MidpointRounding.AwayFromZero);
            var magnitude = units < 0 ? (ulong)(-units) : (ulong)units;

            var fraction = (magnitude % 1000000UL).ToString(CultureInfo.InvariantCulture)
                                                  .PadLeft(6, '0')
                                                  .TrimEnd('0');

            if (fraction.Length == 0)
                fraction = "0";

            return (units < 0 ? "-" : string.Empty) +
                   (magnitude / 1000000UL).ToString(CultureInfo.InvariantCulture) + "." + fraction;
        }

        protected sealed override SensorStatus GetStatus() =>
            _state == PredictionState.Draining ? base.GetStatus() : SensorStatus.OffTime;


        protected sealed override TimeSpan GetValue()
        {
            var samples = Interlocked.Read(ref _samplesCount);
            var speed = ReadChangeSpeed();

            _postedSamples = samples;
            _postedSpeed = speed;

            // Calibration is counted on the SAMPLING clock (#1445): (n/6) means six completed
            // free-space measurements, not six posts.
            if (samples < _calibrationRequests)
            {
                _state = PredictionState.Calibration;

                return MaxPrediction;
            }

            if (speed < 0.0)
            {
                _state = PredictionState.Growing;

                return MaxPrediction;
            }

            if (speed == 0.0)
            {
                _state = PredictionState.NoDrain;

                return MaxPrediction;
            }

            // A read failure here surfaces as an Error post through the base class, like any other
            // monitoring sensor.
            var seconds = FreeSpace / speed;

            // Written so a NaN (an unreadable free space over a denormal speed) also takes the
            // ceiling branch instead of reaching TimeSpan.FromSeconds.
            if (!(seconds < _maxPredictionSeconds))
            {
                _state = PredictionState.BeyondCeiling;

                return MaxPrediction;
            }

            _state = PredictionState.Draining;

            return TimeSpan.FromSeconds(seconds);
        }

        /// <summary>
        /// How long after Start the FIRST free-space measurement is taken: a full sampling period,
        /// never a wall-clock-aligned remainder (#1445).
        ///
        /// The first interval does not fold in at <see cref="SpeedSmoothingFactor"/> - it SEEDS the
        /// estimate, and carries ~95 % of the weight of the first posted prediction. Aligning the
        /// first tick to the next period boundary made that interval anything from a millisecond to
        /// the whole period depending on when the host happened to start the agent, so a log flush
        /// in the wrong three seconds would seed a wildly inflated rate that then decays with a
        /// 4.22 h half-life - the #1445 symptom coming back on every restart. The native mirror
        /// anchors its first refresh at <c>now + refresh_period_ms</c>, so waiting a full period is
        /// also what repo rule #10 requires.
        /// </summary>
        internal static TimeSpan FirstSampleDelay(TimeSpan samplingPeriod) => samplingPeriod;

        internal void UpdateDiskSpeed()
        {
            if (!TryReadFreeSpace(out var curSpace))
                return;

            var utc = UtcNowProvider();
            var elapsedSeconds = _hasBaseline ? (utc - _lastSpeedCheckTime).TotalSeconds : 0.0;

            if (elapsedSeconds > 0.0)
            {
                // SIGNED: positive when free space shrank over the interval, negative when it grew.
                // Every interval is folded in, which is what lets the estimate decay (#1445).
                var curSpeed = (_lastAvailableSpace - curSpace) / elapsedSeconds;
                var samples = Interlocked.Read(ref _samplesCount);
                var smoothed = samples == 0L
                    ? curSpeed
                    : ReadChangeSpeed() * (1.0 - SpeedSmoothingFactor) + curSpeed * SpeedSmoothingFactor;

                Interlocked.Exchange(ref _currentChangeSpeed, smoothed);
                Interlocked.Increment(ref _samplesCount);
            }

            _hasBaseline = true;
            _lastAvailableSpace = curSpace;
            _lastSpeedCheckTime = utc;
        }

        // The sampler and the post loop are different threads, and a double read is not atomic on a
        // 32-bit runtime, so the speed is read the same way it is written.
        private double ReadChangeSpeed() => Interlocked.CompareExchange(ref _currentChangeSpeed, 0.0, 0.0);

        private bool TryReadFreeSpace(out long freeSpace)
        {
            try
            {
                freeSpace = FreeSpace;
                return true;
            }
            catch (Exception ex)
            {
                freeSpace = 0L;
                HandleException(ex);
                return false;
            }
        }


        private enum PredictionState
        {
            /// <summary>Fewer than CalibrationRequests free-space measurements have completed.</summary>
            Calibration,

            /// <summary>Free space is shrinking and the estimate is below the ceiling — the only Ok state.</summary>
            Draining,

            /// <summary>Free space is shrinking so slowly that the disk will not fill within the ceiling.</summary>
            BeyondCeiling,

            /// <summary>The smoothed rate is exactly zero — nothing measurable is being written.</summary>
            NoDrain,

            /// <summary>Free space is growing.</summary>
            Growing,
        }
    }
}
