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
        private const string MonitorPath = ".tcp_connection_failure_rate_monitor.";

        internal const string SensorName = "Connection failures rate";

        private readonly TcpFailureRateAccumulator _accumulator = new TcpFailureRateAccumulator();
        private readonly Func<FamilyReading> _readV4;
        private readonly Func<FamilyReading> _readV6;
        private readonly IMonitoringRateSensor _sensor;
        private readonly SensorsStorage _storage;
        private readonly TimeSpan _period;

        private CancellationTokenSource _cts;
        private Task _loopTask;

        public string SensorPath { get; } = MonitorPath;


        /// <param name="readV4">The cumulative counter of IPv4; a seam for tests, the OS by default.</param>
        /// <param name="readV6">The same for IPv6.</param>
        internal WindowsTcpFailureRateMonitor(SensorsStorage storage, TimeSpan period, Func<FamilyReading> readV4 = null, Func<FamilyReading> readV6 = null)
        {
            if (period <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(period), "The sampling period must be greater than zero.");

            _storage = storage;
            _period = period;
            _readV4 = readV4 ?? (() => Read(properties => properties.GetTcpIPv4Statistics()));
            _readV6 = readV6 ?? (() => Read(properties => properties.GetTcpIPv6Statistics()));

            // Created here, not by the first sample: the sensor is in the storage, and so in the
            // registrations of Start, the moment it is asked for. The loop below only pushes.
            var options = BuildOptions(period);
            var path = DefaultPrototype.RevealDefaultPath(options, "Network", SensorName);

            _sensor = storage.CreateRateSensor(path, options);
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

        private static FamilyReading Read(Func<IPGlobalProperties, TcpStatistics> family)
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


        public ValueTask<bool> InitAsync()
        {
            // The first reading seeds the baselines and posts nothing.
            Sample();
            return new ValueTask<bool>(true);
        }

        public ValueTask<bool> StartAsync()
        {
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
            return new ValueTask<bool>(true);
        }

        public async ValueTask StopAsync()
        {
            _cts?.Cancel();

            if (_loopTask != null)
            {
                try { await _loopTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }

        public void Dispose()
        {
            _cts?.Dispose();
            _cts = null;
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
            try
            {
                // The increase of this period goes into the rate sensor, which posts it over the
                // time it measured. A quiet period pushes nothing, so the posted rate is 0.
                var delta = _accumulator.Accumulate(_readV4(), _readV6());

                if (delta.HasValue)
                    _sensor.AddValue(delta.Value);
            }
            catch (Exception ex)
            {
                _storage.ReportError($"TCP connection failure rate sampling error: {ex.Message}");
            }
        }
    }
}
