using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    /// <summary>
    /// Periodically samples per-interface cumulative byte counters (via
    /// <see cref="NetworkInterface.GetIPStatistics"/>) and posts Double bar sensors at
    /// "Network/&lt;interface-name&gt;/{Received,Sent} MB,sec" for every active (Up, non-loopback)
    /// interface. Sensor pairs are created lazily on first sighting; a disappeared interface
    /// (VPN down, cable unplugged) expires by TTL. Windows only.
    /// </summary>
    internal sealed class WindowsNetworkInterfaceSpeedMonitor : ISensor
    {
        private const string MonitorPath = ".network_interface_speed_monitor.";

        // Sample sub-minutely so the bar (1-minute window) accumulates meaningful min/mean/max.
        private static readonly TimeSpan SamplePeriod = TimeSpan.FromSeconds(10);

        private static readonly BarSensorOptions InterfaceBarOptions = new BarSensorOptions
        {
            IsComputerSensor = true,
            TTL = TimeSpan.FromMinutes(5),
            KeepHistory = TimeSpan.FromDays(90),
            SensorUnit = Unit.MBytes_sec,
            Statistics = StatisticsOptions.EMA,
            BarPeriod = TimeSpan.FromMinutes(1),
            BarTickPeriod = TimeSpan.FromSeconds(15),
            PostDataPeriod = TimeSpan.FromSeconds(15),
        };

        private readonly SensorsStorage _storage;

        // interface name -> bar sensor, per direction. Created independently so a direction with no
        // traffic never spawns an always-zero sensor.
        private readonly Dictionary<string, IBarSensor<double>> _rxSensors = new Dictionary<string, IBarSensor<double>>();
        private readonly Dictionary<string, IBarSensor<double>> _txSensors = new Dictionary<string, IBarSensor<double>>();

        // interface name -> previous cumulative (BytesReceived, BytesSent)
        private readonly Dictionary<string, (long rx, long tx)> _prevBytes =
            new Dictionary<string, (long, long)>();

        private DateTime _prevTime = DateTime.MinValue;
        private CancellationTokenSource _cts;
        private Task _loopTask;

        public string SensorPath { get; } = MonitorPath;


        internal WindowsNetworkInterfaceSpeedMonitor(SensorsStorage storage)
        {
            _storage = storage;
        }


        public ValueTask<bool> InitAsync()
        {
            SeedBaseline();
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
                catch { }
            }
        }

        public void Dispose()
        {
            _cts?.Dispose();
            _cts = null;
        }


        private void SeedBaseline()
        {
            try
            {
                _prevTime = DateTime.UtcNow;
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (!IsActive(nic))
                        continue;
                    try
                    {
                        var stats = nic.GetIPStatistics();
                        _prevBytes[nic.Name] = (stats.BytesReceived, stats.BytesSent);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(SamplePeriod, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }

                try { Sample(); }
                catch { }
            }
        }

        private void Sample()
        {
            var now = DateTime.UtcNow;
            var elapsedSec = (now - _prevTime).TotalSeconds;
            if (elapsedSec <= 0)
                return;

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (!IsActive(nic))
                        continue;

                    try
                    {
                        var stats = nic.GetIPStatistics();
                        var curRx = stats.BytesReceived;
                        var curTx = stats.BytesSent;
                        var name = nic.Name;

                        if (_prevBytes.TryGetValue(name, out var prev))
                        {
                            var deltaRx = curRx - prev.rx;
                            var deltaTx = curTx - prev.tx;

                            // Post each direction ONLY when it carried traffic this interval (>0):
                            // negative delta (counter reset) and zero delta (idle) are both skipped, so
                            // an idle direction never creates a permanently-zero sensor. A direction
                            // that goes quiet stops posting and expires by TTL.
                            if (deltaRx > 0)
                                GetOrCreate(_rxSensors, name, "Received MB,sec", "Average received")
                                    .AddValue(deltaRx / elapsedSec / (1024.0 * 1024.0));
                            if (deltaTx > 0)
                                GetOrCreate(_txSensors, name, "Sent MB,sec", "Average sent")
                                    .AddValue(deltaTx / elapsedSec / (1024.0 * 1024.0));
                        }

                        _prevBytes[name] = (curRx, curTx);
                    }
                    catch { }
                }
            }
            catch { }

            _prevTime = now;
        }

        private IBarSensor<double> GetOrCreate(Dictionary<string, IBarSensor<double>> sensors, string name, string leaf, string descr)
        {
            if (sensors.TryGetValue(name, out var existing))
                return existing;

            var opts = (BarSensorOptions)InterfaceBarOptions.Copy();
            opts.Description = $"{descr} network speed on interface **{name}**. " + InterfaceBarOptions.GetBarOptionsInfo();

            // Leaf uses "MB,sec" (not "MB/sec") so the unit isn't split into an extra tree node.
            var sensor = (IBarSensor<double>)_storage.CreateDoubleBarSensor($"Network/{name}/{leaf}", opts);
            sensors[name] = sensor;
            return sensor;
        }

        private static bool IsActive(NetworkInterface nic) =>
            nic.OperationalStatus == OperationalStatus.Up &&
            nic.NetworkInterfaceType != NetworkInterfaceType.Loopback;
    }
}
