using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using SysProcess = System.Diagnostics.Process;


namespace HSMDataCollector.DefaultSensors.Windows.Process
{
    /// <summary>
    /// Periodically samples per-process CPU usage and posts Double sensors at
    /// "Top CPU processes/&lt;exe-name&gt;" for the busiest N processes above a minimum threshold.
    /// Implements ISensor so the collector's lifecycle (InitAsync/StartAsync/StopAsync) drives it.
    /// </summary>
    internal sealed class WindowsTopCpuMonitor : ISensor
    {
        // Fixed internal path — used only as a dedup key in SensorsStorage; never posted to the server.
        private const string MonitorPath = ".top_cpu_group_monitor.";

        private readonly SensorsStorage _storage;
        private readonly int _count;
        private readonly double _minPercent;
        private readonly TimeSpan _period;

        // name -> sensor handle (created lazily on first appearance)
        private readonly Dictionary<string, IInstantValueSensor<double>> _sensors =
            new Dictionary<string, IInstantValueSensor<double>>();
        // name -> first seen full path (MainModule.FileName), cached to survive process exit
        private readonly Dictionary<string, string> _fullPaths =
            new Dictionary<string, string>(StringComparer.Ordinal);
        // (pid, startTimeTicks) -> previous TotalProcessorTime in milliseconds
        private readonly Dictionary<long, double> _prevCpu = new Dictionary<long, double>();
        private DateTime _prevTime = DateTime.MinValue;

        private CancellationTokenSource _cts;
        private Task _loopTask;

        public string SensorPath { get; } = MonitorPath;

        internal WindowsTopCpuMonitor(SensorsStorage storage, int count, double minPercent, TimeSpan period)
        {
            _storage = storage;
            _count = count;
            _minPercent = minPercent;
            _period = period;
        }

        public ValueTask<bool> InitAsync()
        {
            _prevTime = DateTime.UtcNow;
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
                foreach (var p in SysProcess.GetProcesses())
                {
                    try
                    {
                        var key = MakeKey(p.Id, p.StartTime.ToUniversalTime().Ticks);
                        _prevCpu[key] = p.TotalProcessorTime.TotalMilliseconds;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(_period, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }

                try { Sample(); }
                catch { }
            }
        }

        private void Sample()
        {
            var now = DateTime.UtcNow;
            var elapsedMs = (now - _prevTime).TotalMilliseconds;
            if (elapsedMs <= 0)
                return;

            var byName = new Dictionary<string, double>(StringComparer.Ordinal);
            var curCpu = new Dictionary<long, double>();

            foreach (var p in SysProcess.GetProcesses())
            {
                try
                {
                    var key = MakeKey(p.Id, p.StartTime.ToUniversalTime().Ticks);
                    var cpuMs = p.TotalProcessorTime.TotalMilliseconds;
                    curCpu[key] = cpuMs;

                    // Cache full path on first encounter — MainModule can throw for protected processes.
                    if (!_fullPaths.ContainsKey(p.ProcessName))
                    {
                        try { _fullPaths[p.ProcessName] = p.MainModule.FileName; }
                        catch { }
                    }

                    double prevMs;
                    if (_prevCpu.TryGetValue(key, out prevMs))
                    {
                        var deltaCpu = cpuMs - prevMs;
                        var percent = deltaCpu / (elapsedMs * Environment.ProcessorCount) * 100.0;
                        if (percent > 0)
                        {
                            double existing;
                            if (!byName.TryGetValue(p.ProcessName, out existing))
                                existing = 0;
                            byName[p.ProcessName] = existing + percent;
                        }
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }

            _prevTime = now;
            _prevCpu.Clear();
            foreach (var kv in curCpu)
                _prevCpu[kv.Key] = kv.Value;

            var top = byName
                .Where(kv => kv.Value >= _minPercent)
                .OrderByDescending(kv => kv.Value)
                .Take(_count);

            foreach (var kv in top)
            {
                var name = kv.Key;
                var percent = kv.Value;

                IInstantValueSensor<double> sensor;
                if (!_sensors.TryGetValue(name, out sensor))
                {
                    string fullPath;
                    _fullPaths.TryGetValue(name, out fullPath);
                    var descSuffix = string.IsNullOrEmpty(fullPath) ? "" : "; " + fullPath;
                    sensor = _storage.CreateInstantSensor<double>(
                        "Top CPU processes/" + name,
                        new InstantSensorOptions
                        {
                            Description = string.Format(
                                "Top CPU processes/{0} — top {1} consumers by % of machine CPU{2}",
                                name, _count, descSuffix)
                        });
                    _sensors[name] = sensor;
                }
                sensor.AddValue(percent);
            }
        }

        // Combine pid + startTimeTicks into a single long key to avoid tuple syntax issues on net472.
        private static long MakeKey(int pid, long startTicks) => ((long)pid << 32) ^ startTicks;
    }
}
