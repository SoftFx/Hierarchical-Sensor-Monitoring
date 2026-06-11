using System;
using System.Collections.Generic;
using System.Linq;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.DefaultSensors.Windows;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1102-E1: multi-instance performance-counter resolution. The legacy logic bound the first
    /// instance whose name CONTAINS the filter, once, forever — so "App" could bind to "AppService"
    /// (another process), and an instance-index reshuffle after a neighbor process exit silently
    /// switched the counter to another process's data. Per-process categories must bind by PID and
    /// re-validate the binding on every read; name-only categories must prefer an exact name match.
    /// </summary>
    public sealed class PerformanceCounterInstanceResolutionTests
    {
        private const string Category = "Process";
        private const string PidCounter = "ID Process";
        private const string ValueCounter = "% Processor Time";
        private const int OwnPid = 4242;

        [Fact]
        public void Pid_category_binds_to_instance_with_matching_pid_not_first_name_match()
        {
            // "App" (another process) sorts before "App#1" (ours) and contains the filter.
            var source = new FakeCounterSource(OwnPid)
                .WithInstance("App", pid: 999, value: 11.0)
                .WithInstance("AppService", pid: 1000, value: 22.0)
                .WithInstance("App#1", pid: OwnPid, value: 33.0);

            using (var counter = CreateProcessAwareCounter(source, "App"))
            {
                Assert.NotNull(counter);
                Assert.Equal(33.0, counter.NextValue());
            }
        }

        [Fact]
        public void Pid_category_returns_null_when_no_instance_has_matching_pid()
        {
            var source = new FakeCounterSource(OwnPid)
                .WithInstance("App", pid: 999, value: 11.0)
                .WithInstance("AppService", pid: 1000, value: 22.0);

            var counter = CreateProcessAwareCounter(source, "App");

            Assert.Null(counter);
        }

        [Fact]
        public void Instance_reshuffle_rebinds_to_the_instance_that_now_hosts_our_pid()
        {
            var source = new FakeCounterSource(OwnPid)
                .WithInstance("App", pid: 999, value: 11.0)
                .WithInstance("App#1", pid: OwnPid, value: 33.0);

            using (var counter = CreateProcessAwareCounter(source, "App"))
            {
                Assert.Equal(33.0, counter.NextValue());

                // Neighbor "App" exits -> Windows renames "App#1" to "App" (index reshuffle).
                // The old binding "App#1" no longer exists; reading it returns ANOTHER process's data
                // unless the binding is re-validated.
                source.Reset()
                    .WithInstance("App", pid: OwnPid, value: 55.0);

                Assert.Equal(55.0, counter.NextValue());
            }
        }

        [Fact]
        public void Reshuffle_that_reuses_the_bound_instance_name_for_another_process_is_detected()
        {
            var source = new FakeCounterSource(OwnPid)
                .WithInstance("App", pid: OwnPid, value: 33.0)
                .WithInstance("App#1", pid: 999, value: 11.0);

            using (var counter = CreateProcessAwareCounter(source, "App"))
            {
                Assert.Equal(33.0, counter.NextValue());

                // Our instance got renamed to "App#1" while a NEW process took the name "App":
                // the bound name still exists, but it now belongs to someone else.
                source.Reset()
                    .WithInstance("App", pid: 999, value: 11.0)
                    .WithInstance("App#1", pid: OwnPid, value: 77.0);

                Assert.Equal(77.0, counter.NextValue());
            }
        }

        [Fact]
        public void Name_resolution_prefers_exact_match_over_contains()
        {
            var instances = new[] { "AppService", "App" };

            Assert.Equal("App", PerformanceCounterInstanceResolver.ResolveByName(instances, "App"));
        }

        [Fact]
        public void Name_resolution_falls_back_to_contains_match()
        {
            var instances = new[] { "C: D:", "E:" };

            Assert.Equal("C: D:", PerformanceCounterInstanceResolver.ResolveByName(instances, "C:"));
        }

        [Fact]
        public void Name_resolution_returns_null_when_nothing_matches()
        {
            Assert.Null(PerformanceCounterInstanceResolver.ResolveByName(new[] { "X", "Y" }, "App"));
        }

        [Fact]
        public void Process_and_clr_categories_are_pid_resolvable()
        {
            Assert.True(PerformanceCounterInstanceResolver.TryGetPidCounterName("Process", out var processPid));
            Assert.Equal("ID Process", processPid);

            Assert.True(PerformanceCounterInstanceResolver.TryGetPidCounterName(".NET CLR Memory", out var clrPid));
            Assert.Equal("Process ID", clrPid);

            Assert.False(PerformanceCounterInstanceResolver.TryGetPidCounterName("Processor", out _));
            Assert.False(PerformanceCounterInstanceResolver.TryGetPidCounterName("LogicalDisk", out _));
        }

        [Fact]
        public void Repeated_reshuffles_never_return_another_process_value()
        {
            // Churn scenario: neighbor processes start and exit, so our PID keeps migrating between
            // instance names while foreign instances always match the filter too. Every single read
            // must return OUR value (42), never a neighbor's (13).
            var source = new FakeCounterSource(OwnPid)
                .WithInstance("App", pid: OwnPid, value: 42.0)
                .WithInstance("App#1", pid: 901, value: 13.0);

            using (var counter = CreateProcessAwareCounter(source, "App"))
            {
                var ownInstanceNames = new[] { "App", "App#1", "App#2" };

                for (var round = 0; round < 100; round++)
                {
                    var ownName = ownInstanceNames[round % ownInstanceNames.Length];

                    source.Reset();
                    source.WithInstance(ownName, pid: OwnPid, value: 42.0);

                    foreach (var name in ownInstanceNames)
                    {
                        if (name != ownName)
                            source.WithInstance(name, pid: 900 + round, value: 13.0);
                    }

                    Assert.Equal(42.0, counter.NextValue());
                }
            }
        }

        [Fact]
        public void Rebinding_failure_throws_instead_of_returning_foreign_data()
        {
            var source = new FakeCounterSource(OwnPid)
                .WithInstance("App", pid: OwnPid, value: 42.0);

            using (var counter = CreateProcessAwareCounter(source, "App"))
            {
                Assert.Equal(42.0, counter.NextValue());

                // Our process's instance disappears entirely (e.g. counters break); a foreign
                // instance still matches the name filter. The read must fail loudly, not silently
                // bind to the neighbor.
                source.Reset()
                    .WithInstance("App", pid: 999, value: 13.0);

                Assert.Throws<InvalidOperationException>(() => counter.NextValue());
            }
        }

        [Fact]
        public void Read_after_failed_rebinding_recovers_once_our_instance_returns()
        {
            var source = new FakeCounterSource(OwnPid)
                .WithInstance("App", pid: OwnPid, value: 42.0);

            using (var counter = CreateProcessAwareCounter(source, "App"))
            {
                Assert.Equal(42.0, counter.NextValue());

                source.Reset().WithInstance("App", pid: 999, value: 13.0);
                Assert.Throws<InvalidOperationException>(() => counter.NextValue());

                // The instance comes back (counters repaired / process re-registered).
                source.Reset().WithInstance("App#1", pid: OwnPid, value: 77.0);

                Assert.Equal(77.0, counter.NextValue());
            }
        }

        [Fact]
        public void Disposing_the_counter_disposes_underlying_counters()
        {
            var source = new FakeCounterSource(OwnPid)
                .WithInstance("App", pid: OwnPid, value: 33.0);

            var counter = CreateProcessAwareCounter(source, "App");
            counter.NextValue();
            counter.Dispose();

            Assert.Equal(0, source.LiveCounters);
        }

        private static ProcessAwarePerformanceCounter CreateProcessAwareCounter(FakeCounterSource source, string filter) =>
            ProcessAwarePerformanceCounter.TryCreate(source, Category, ValueCounter, PidCounter, filter, OwnPid);


        private sealed class FakeCounterSource : IPerformanceCounterSource
        {
            private readonly Dictionary<string, (int Pid, double Value)> _instances = new Dictionary<string, (int, double)>(StringComparer.Ordinal);
            private int _liveCounters;

            internal FakeCounterSource(int ownPid)
            {
                _ = ownPid;
            }

            internal int LiveCounters => _liveCounters;

            internal FakeCounterSource WithInstance(string name, int pid, double value)
            {
                _instances[name] = (pid, value);
                return this;
            }

            internal FakeCounterSource Reset()
            {
                _instances.Clear();
                return this;
            }

            public string[] GetInstanceNames(string category) => _instances.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();

            public IPerformanceCounter Create(string category, string counter, string instance) =>
                new FakeCounter(this, counter, instance);


            private sealed class FakeCounter : IPerformanceCounter
            {
                private readonly FakeCounterSource _source;
                private readonly string _counter;
                private readonly string _instance;
                private bool _disposed;

                internal FakeCounter(FakeCounterSource source, string counter, string instance)
                {
                    _source = source;
                    _counter = counter;
                    _instance = instance;
                    System.Threading.Interlocked.Increment(ref source._liveCounters);
                }

                public double NextValue()
                {
                    if (!_source._instances.TryGetValue(_instance, out var data))
                        throw new InvalidOperationException($"Instance '{_instance}' does not exist (the real PerformanceCounter throws once the instance disappears).");

                    return _counter == PidCounter ? data.Pid : data.Value;
                }

                public void Dispose()
                {
                    if (_disposed)
                        return;

                    _disposed = true;
                    System.Threading.Interlocked.Decrement(ref _source._liveCounters);
                }
            }
        }
    }
}
