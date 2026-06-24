using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
        // Group-aware processor count: Environment.ProcessorCount only sees the current processor
        // group (≤64) on net472, which would over-report CPU% on large multi-group servers.
        [DllImport("kernel32.dll")]
        private static extern uint GetActiveProcessorCount(ushort groupNumber);
        private const ushort AllProcessorGroups = 0xffff;

        private static int GetTotalProcessorCount()
        {
            try { return (int)GetActiveProcessorCount(AllProcessorGroups); }
            catch { return Environment.ProcessorCount; }
        }

        // Composite (pid, startTicks) key — avoids XOR collision from simple long packing.
        private readonly struct ProcessKey : IEquatable<ProcessKey>
        {
            public readonly int Pid;
            public readonly long StartTicks;
            public ProcessKey(int pid, long startTicks) { Pid = pid; StartTicks = startTicks; }
            public bool Equals(ProcessKey other) => Pid == other.Pid && StartTicks == other.StartTicks;
            public override bool Equals(object obj) => obj is ProcessKey k && Equals(k);
            public override int GetHashCode() => unchecked(Pid * 397 ^ (int)(StartTicks ^ (StartTicks >> 32)));
        }

        // Fixed internal path — used only as a dedup key in SensorsStorage; never posted to the server.
        private const string MonitorPath = ".top_cpu_group_monitor.";

        private readonly SensorsStorage _storage;
        private readonly int _count;
        private readonly double _minPercent;
        private readonly TimeSpan _period;
        private readonly int _processorCount;

        // Dedicated cap on the number of distinct "Top CPU processes/<name>" sensors this feature
        // may ever create. The server sensor registry is permanent, so without a bound a host that
        // churns through distinctly named processes (CI/build agents, batch jobs) would grow the
        // namespace without limit. 8x the per-tick count (min 64) leaves generous headroom for
        // normal rotation; once reached, new names are skipped and already-tracked names keep
        // updating. Mirrors the native collector's max_tracked_names bound.
        private readonly int _maxTrackedNames;

        // name -> sensor handle (created lazily on first appearance)
        private readonly Dictionary<string, IInstantValueSensor<double>> _sensors =
            new Dictionary<string, IInstantValueSensor<double>>();
        // name -> first seen full path (MainModule.FileName), cached to survive process exit
        private readonly Dictionary<string, string> _fullPaths =
            new Dictionary<string, string>(StringComparer.Ordinal);
        // (pid, startTimeTicks) -> previous TotalProcessorTime in milliseconds
        private readonly Dictionary<ProcessKey, double> _prevCpu = new Dictionary<ProcessKey, double>();
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
            _processorCount = GetTotalProcessorCount();
            _maxTrackedNames = Math.Max(count * 8, 64);
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
                        _prevCpu[new ProcessKey(p.Id, p.StartTime.ToUniversalTime().Ticks)] =
                            p.TotalProcessorTime.TotalMilliseconds;
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
            var curCpu = new Dictionary<ProcessKey, double>();

            foreach (var p in SysProcess.GetProcesses())
            {
                try
                {
                    var key = new ProcessKey(p.Id, p.StartTime.ToUniversalTime().Ticks);
                    var cpuMs = p.TotalProcessorTime.TotalMilliseconds;
                    curCpu[key] = cpuMs;

                    // Cache full path on first encounter — MainModule can throw for protected processes.
                    // Bounded by the same cap as the sensor map so this dictionary can't grow without limit.
                    if (_fullPaths.Count < _maxTrackedNames && !_fullPaths.ContainsKey(p.ProcessName))
                    {
                        try { _fullPaths[p.ProcessName] = p.MainModule.FileName; }
                        catch { }
                    }

                    double prevMs;
                    if (_prevCpu.TryGetValue(key, out prevMs))
                    {
                        var deltaCpu = cpuMs - prevMs;
                        var percent = deltaCpu / (elapsedMs * _processorCount) * 100.0;
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

            // Busiest first; deterministic tie-break by name ascending (mirrors native SelectTopN).
            var top = byName
                .Where(kv => kv.Value >= _minPercent)
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(_count);

            foreach (var kv in top)
            {
                var name = kv.Key;
                var percent = kv.Value;

                IInstantValueSensor<double> sensor;
                if (!_sensors.TryGetValue(name, out sensor))
                {
                    // Dedicated namespace cap: stop creating new per-process sensors once the bound
                    // is reached so transient processes can't grow the server namespace without limit.
                    if (_sensors.Count >= _maxTrackedNames)
                        continue;

                    string fullPath;
                    _fullPaths.TryGetValue(name, out fullPath);
                    var pathLine = string.IsNullOrEmpty(fullPath) ? "" : "\n\n**Path:** `" + fullPath + "`";
                    sensor = _storage.CreateInstantSensor<double>(
                        "Top CPU processes/" + name,
                        new InstantSensorOptions
                        {
                            Description = "Top **" + _count + "** CPU consumers by % of machine CPU" + pathLine
                        });
                    _sensors[name] = sensor;
                }
                sensor.AddValue(percent);
            }
        }

    }
}
