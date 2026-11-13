using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Alerts;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.Prototypes;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using FamilyReading = HSMDataCollector.DefaultSensors.Windows.Network.TcpFailureRateAccumulator.FamilyReading;


namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    /// <summary>
    /// The rate of failed TCP connection attempts, at
    /// ".computer/Network/Connection failures rate" (Windows only). Every period it reads the
    /// cumulative failed-attempt counter of IPv4 and IPv6 and pushes the increase into a rate
    /// sensor, which posts it as failures per second, shown per minute.
    ///
    /// The managed counterpart of the native collector's sensor of the same path
    /// (<c>hsm_collector_enable_tcp_connection_failure_rate_sensor</c>). Root rule 10: both read
    /// one OS source, <c>GetTcpStatisticsEx</c> / <c>MIB_TCPSTATS.dwAttemptFails</c>. Here it is
    /// reached through <see cref="IPGlobalProperties"/>, whose Windows implementation calls that
    /// function for each address family; the performance-counter sensor
    /// <c>Connection Failures Count</c> is a different facade and is left as it is.
    /// </summary>
    internal sealed class WindowsTcpFailureRateMonitor : ISensor
    {
        // A key in SensorsStorage only; never posted to the server.
        internal const string MonitorPath = ".tcp_connection_failure_rate_monitor.";

        // Task.Delay takes no more than this, and counts in whole milliseconds: a shorter
        // period would be a wait of zero and a loop that never yields. A millisecond is the
        // native collector's minimum as well.
        private static readonly TimeSpan MaxPeriod = TimeSpan.FromMilliseconds(int.MaxValue);
        private static readonly TimeSpan MinPeriod = TimeSpan.FromMilliseconds(1);

        internal const string SensorName = "Connection failures rate";

        // One sample at a time. A monitor added while the collector is starting is initialized
        // twice (by its registration and by the Start under way), and the loop of the first
        // start may still be sampling: the accumulator is no thread-safe type, and a baseline
        // read by two samples at once would lose or repeat the increase of a period.
        private readonly object _sampleLock = new object();

        private TcpFailureRateAccumulator _accumulator = new TcpFailureRateAccumulator();
        private double _pushedTotal;
        private readonly Func<FamilyReading> _readV4;
        private readonly Func<FamilyReading> _readV6;
        private readonly IMonitoringRateSensor _sensor;
        private readonly SensorsStorage _storage;
        private readonly TimeSpan _period;

        // Start, Stop and Dispose swap the token source and the loop task as one step. The two
        // starts of a monitor added while the collector is starting come from two threads (the
        // host's, through the registration, and the Start under way): without this, each could
        // leave a token source the other never saw, and its loop would outlive every stop.
        // Not the sample lock: a slow sample must not hold a stop.
        private readonly object _lifecycleLock = new object();

        private CancellationTokenSource _cts;
        private Task _loopTask;
        private bool _disposed;

        public string SensorPath { get; } = MonitorPath;

        /// <summary>The sampling period this monitor was built with.</summary>
        internal TimeSpan Period => _period;


        /// <param name="readV4">The cumulative counter of IPv4; a seam for tests, the OS by default.</param>
        /// <param name="readV6">The same for IPv6.</param>
        internal WindowsTcpFailureRateMonitor(SensorsStorage storage, TimeSpan period, Func<FamilyReading> readV4 = null, Func<FamilyReading> readV6 = null)
        {
            EnsurePeriod(period);

            _storage = storage;
            _period = period;
            _readV4 = readV4 ?? (() => ReadFamily(properties => properties.GetTcpIPv4Statistics()));
            _readV6 = readV6 ?? (() => ReadFamily(properties => properties.GetTcpIPv6Statistics()));

            // Created here, not by the first sample: the sensor is in the storage, and so in the
            // registrations of Start, the moment it is asked for. The loop below only pushes.
            var options = BuildOptions(period);
            var path = DefaultPrototype.RevealDefaultPath(options, "Network", SensorName);

            _sensor = storage.CreateRateSensor(path, options);
        }


        /// <summary>Throws for a period the monitor cannot run with; the same answer on every call.</summary>
        internal static void EnsurePeriod(TimeSpan period)
        {
            if (period < MinPeriod || period > MaxPeriod)
                throw new ArgumentOutOfRangeException(nameof(period), $"The sampling period must be at least a millisecond and at most {MaxPeriod}.");
        }

        // The registration the native collector sends for this sensor (StartTcpFailureRateSampler),
        // field for field; the conformance fixture collector/windows/tcp_failure_rate_contract
        // holds both to it.
        private static RateSensorOptions BuildOptions(TimeSpan period) =>
            new RateSensorOptions
            {
                IsComputerSensor = true,
                EnableForGrafana = true,
                PostDataPeriod = period,
                DisplayUnit = RateDisplayUnit.PerMinute,
                Description = "Failed TCP connection attempts per minute (MIB_TCPSTATS.dwAttemptFails, IPv4+IPv6).",

                // A failure rate that stays high is a real problem: a service in a crash loop, port
                // exhaustion, a client hammering a dead port. More than 1 failed attempt per second
                // (60 per minute) for 5 minutes raises the sensor to Error and notifies; the
                // confirmation period keeps a short spike quiet. The threshold is in the sensor's
                // base unit, failures per second.
                Alerts = new List<InstantAlertTemplate>
                {
                    AlertsFactory.IfValue(AlertOperation.GreaterThan, 1)
                                 .AndConfirmationPeriod(TimeSpan.FromMinutes(5))
                                 .ThenSendNotification("[$product] TCP connection failures elevated: $value/sec failed connection attempts")
                                 .AndSetIcon(AlertIcon.Error)
                                 .AndSetSensorError()
                                 .Build(),
                },
            };

        internal static FamilyReading ReadFamily(Func<IPGlobalProperties, TcpStatistics> family)
        {
            try
            {
                return new FamilyReading((ulong)family(IPGlobalProperties.GetIPGlobalProperties()).FailedConnectionAttempts);
            }
            catch (Exception)
            {
                // The read failed for this family (IPv6 not installed, for one): it sits this tick out.
                return FamilyReading.Unreadable;
            }
        }


        /// <summary>The failed attempts pushed into the rate sensor so far. For tests.</summary>
        internal double PushedTotal => Volatile.Read(ref _pushedTotal);

        public ValueTask<bool> InitAsync()
        {
            // Every run starts from baselines of its own, as the native collector builds a new
            // sampler on each Start: with the baselines of the previous run, everything that
            // failed while the collector was stopped would come out as one spike. The first
            // reading only seeds them and posts nothing.
            lock (_sampleLock)
            {
                _accumulator = new TcpFailureRateAccumulator();
                Sample();
            }

            return new ValueTask<bool>(true);
        }

        public ValueTask<bool> StartAsync()
        {
            // A monitor added while the collector is starting is started twice: once by its
            // registration and once by the Start that is under way. The loop of the first start
            // is ended, and the new one begins only when it has: one loop at a time owns the
            // accumulator, and none is left running behind a token nobody can cancel.
            lock (_lifecycleLock)
            {
                // A start that lost the race with Dispose begins nothing.
                if (_disposed)
                    return new ValueTask<bool>(false);

                var previous = _loopTask;

                try { _cts?.Cancel(); }
                catch (ObjectDisposedException) { }

                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                var token = _cts.Token;

                _loopTask = Task.Run(async () =>
                {
                    if (previous != null)
                    {
                        try { await previous.ConfigureAwait(false); }
                        catch (Exception) { }
                    }

                    await RunLoopAsync(token).ConfigureAwait(false);
                });
            }

            return new ValueTask<bool>(true);
        }

        public async ValueTask StopAsync()
        {
            Task loop;

            lock (_lifecycleLock)
            {
                try { _cts?.Cancel(); }
                catch (ObjectDisposedException) { }

                loop = _loopTask;
            }

            if (loop == null)
                return;

            // The loop ends on the cancellation and catches what a sample throws; whatever else
            // could come out of it must not fault the collector's stop.
            try { await loop.ConfigureAwait(false); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            lock (_lifecycleLock)
            {
                _disposed = true;

                // Cancelled first: a monitor disposed without a stop must not leave its loop running.
                try { _cts?.Cancel(); }
                catch (ObjectDisposedException) { }

                _cts?.Dispose();
                _cts = null;
            }
        }


        private async Task RunLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(_period, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }

                Sample();
            }
        }

        private void Sample()
        {
            lock (_sampleLock)
                SampleLocked();
        }

        private void SampleLocked()
        {
            try
            {
                var v4 = _readV4();
                var v6 = _readV6();

                // One family that cannot be read sits the tick out quietly (IPv6 not installed).
                // With neither there is nothing behind the 0 the sensor goes on posting, and
                // that must not pass for a quiet network. Deduplicated like every other error.
                if (!v4.Readable && !v6.Readable)
                    _storage.ReportError("TCP connection failure rate sampling error: the TCP statistics of neither IPv4 nor IPv6 could be read.");

                // The increase of this period goes into the rate sensor, which posts it over the
                // time it measured. A quiet period pushes nothing, so the posted rate is 0.
                var delta = _accumulator.Accumulate(v4, v6);

                if (delta.HasValue)
                {
                    _sensor.AddValue(delta.Value);
                    Volatile.Write(ref _pushedTotal, _pushedTotal + delta.Value);
                }
            }
            catch (Exception ex)
            {
                _storage.ReportError($"TCP connection failure rate sampling error: {ex.Message}");
            }
        }
    }
}
