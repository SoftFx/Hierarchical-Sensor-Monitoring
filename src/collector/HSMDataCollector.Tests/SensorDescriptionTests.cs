using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// Changing a created sensor's description (#1482), the managed counterpart of the native
    /// hsm_sensor_set_description: the re-registration rules per lifecycle state, null/empty text,
    /// every sensor kind, the backward-compatible API shape and concurrent use. The portable part is
    /// also pinned cross-language by registration_contract.hsmtest (set_description_* cases).
    /// </summary>
    public sealed class SensorDescriptionTests
    {
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

        private static DataCollector CreateCollector(IDataSender sender) =>
            new DataCollector(new CollectorOptions
            {
                AccessKey = "describe-key",
                ClientName = "describe-client",
                ComputerName = "describe-host",
                Module = "describe-module",
                DataSender = sender,
                MaxQueueSize = 100000,
                MaxValuesInPackage = 1000,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(20),
                RequestTimeout = TimeSpan.FromSeconds(1),
            });

        private static InstantSensorOptions Options(string description) => new InstantSensorOptions
        {
            Description = description,
            TTL = TimeSpan.FromMinutes(1),
            SensorUnit = Unit.bytes,
        };

        // --- Lifecycle states ---

        [Fact]
        public async Task Before_start_the_new_text_rides_the_start_registration()
        {
            var sender = new RegistrationSender();
            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateIntSensor("describe/before", Options("first"));

                Assert.True(sensor.SetDescription("second"));
                await Task.Delay(100).ConfigureAwait(false);
                Assert.Empty(sender.Registrations);

                await collector.Start().ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);

                var registration = Assert.Single(sender.Registrations);
                Assert.Equal("second", registration.Description);
            }
        }

        [Fact]
        public async Task While_running_the_sensor_is_re_registered_with_its_other_options_kept()
        {
            var sender = new RegistrationSender();
            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateIntSensor("describe/running", Options("first"));
                await collector.Start().ConfigureAwait(false);
                Assert.True(await sender.WaitForRegistrationsAsync(1, WaitTimeout).ConfigureAwait(false));

                Assert.True(sensor.SetDescription("limit 2048 MB"));
                Assert.True(await sender.WaitForRegistrationsAsync(2, WaitTimeout).ConfigureAwait(false));

                var first = sender.Registrations[0];
                var second = sender.Registrations[1];
                Assert.Equal("first", first.Description);
                Assert.Equal("limit 2048 MB", second.Description);
                Assert.Equal(first.Path, second.Path);
                Assert.Equal(first.SensorType, second.SensorType);
                Assert.Equal(first.TTLs, second.TTLs);
                Assert.Equal(first.OriginalUnit, second.OriginalUnit);

                await collector.Stop().ConfigureAwait(false);
                Assert.Equal(2, sender.Registrations.Count);
            }
        }

        [Fact]
        public async Task While_running_an_unchanged_text_still_re_registers()
        {
            // Native SetDescription rebuilds and bumps the registration version on every call, so the
            // HTTP transport re-posts it even when the text is the same; managed mirrors that.
            var sender = new RegistrationSender();
            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateIntSensor("describe/same", Options("same"));
                await collector.Start().ConfigureAwait(false);
                Assert.True(await sender.WaitForRegistrationsAsync(1, WaitTimeout).ConfigureAwait(false));

                Assert.True(sensor.SetDescription("same"));
                Assert.True(await sender.WaitForRegistrationsAsync(2, WaitTimeout).ConfigureAwait(false));
                Assert.All(sender.Registrations, r => Assert.Equal("same", r.Description));

                await collector.Stop().ConfigureAwait(false);
            }
        }

        [Fact]
        public async Task While_stopped_nothing_is_sent_and_the_next_start_registers_the_new_text()
        {
            var sender = new RegistrationSender();
            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateIntSensor("describe/stopped", Options("first"));
                await collector.Start().ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);
                Assert.Single(sender.Registrations);

                Assert.True(sensor.SetDescription("second"));
                await Task.Delay(100).ConfigureAwait(false);
                Assert.Single(sender.Registrations);

                await collector.Start().ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);

                Assert.Equal(2, sender.Registrations.Count);
                Assert.Equal("first", sender.Registrations[0].Description);
                Assert.Equal("second", sender.Registrations[1].Description);
            }
        }

        [Fact]
        public async Task While_stopping_nothing_is_sent_and_the_next_start_registers_the_new_text()
        {
            var sender = new RegistrationSender();
            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateIntSensor("describe/stopping", Options("first"));
                await collector.Start().ConfigureAwait(false);
                Assert.True(await sender.WaitForRegistrationsAsync(1, WaitTimeout).ConfigureAwait(false));

                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var stopTask = collector.Stop(release.Task);
                Assert.Equal(CollectorStatus.Stopping, collector.Status);

                Assert.True(sensor.SetDescription("second"));

                release.SetResult(true);
                await stopTask.ConfigureAwait(false);
                Assert.Single(sender.Registrations);

                await collector.Start().ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);
                Assert.Equal("second", sender.Registrations.Last().Description);
            }
        }

        [Fact]
        public async Task After_the_collector_is_disposed_the_call_succeeds_and_sends_nothing()
        {
            // Native: once the collector is destroyed the sensor's owning collector is gone, so the
            // text is replaced and nothing is emitted; the call still returns OK.
            var sender = new RegistrationSender();
            var collector = CreateCollector(sender);
            var sensor = collector.CreateIntSensor("describe/disposed", Options("first"));
            await collector.Start().ConfigureAwait(false);
            Assert.True(await sender.WaitForRegistrationsAsync(1, WaitTimeout).ConfigureAwait(false));

            collector.Dispose();
            var sentBefore = sender.Registrations.Count;

            Assert.True(sensor.SetDescription("second"));
            await Task.Delay(100).ConfigureAwait(false);
            Assert.Equal(sentBefore, sender.Registrations.Count);
        }

        // --- Sensors the collector does not hold (PR #1503 review) ---

        [Fact]
        public async Task A_sensor_rejected_while_stopping_is_never_registered_by_a_later_call()
        {
            // SensorsStorage.Register disposes a sensor created while the collector stops and returns
            // it inert. A later description change must not register a path no value will reach.
            var sender = new RegistrationSender();
            using (var collector = CreateCollector(sender))
            {
                await collector.Start().ConfigureAwait(false);

                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var stopTask = collector.Stop(release.Task);
                Assert.Equal(CollectorStatus.Stopping, collector.Status);

                var rejected = collector.CreateIntSensor("describe/rejected", Options("first"));

                release.SetResult(true);
                await stopTask.ConfigureAwait(false);
                await collector.Start().ConfigureAwait(false);

                Assert.False(rejected.SetDescription("second"));
                await collector.Stop().ConfigureAwait(false);

                Assert.DoesNotContain(sender.Registrations, r => r.Path.EndsWith("describe/rejected", StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task A_sensor_removed_from_the_collector_is_never_registered_by_a_later_call()
        {
            // The removal path the collector itself uses (DefaultSensorsCollection.Unregister ->
            // SensorsStorage.TryRemove, e.g. UnsubscribeWindowsServiceStatus), driven directly because
            // that public call hands out no sensor handle.
            var sender = new RegistrationSender();
            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateIntSensor("describe/removed", Options("first"));
                await collector.Start().ConfigureAwait(false);
                Assert.True(await sender.WaitForRegistrationsAsync(1, WaitTimeout).ConfigureAwait(false));

                Assert.True(StorageOf(collector).TryRemove(((ISensor)sensor).SensorPath, out _));

                Assert.False(sensor.SetDescription("second"));
                await collector.Stop().ConfigureAwait(false);
                await collector.Start().ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);

                var registration = Assert.Single(sender.Registrations);
                Assert.Equal("first", registration.Description);
            }
        }

        [Fact]
        public async Task A_sensor_whose_handle_was_disposed_is_still_the_collectors_and_takes_the_new_text()
        {
            // Disposing a handle stops the sensor but leaves it in the collector's storage, and the
            // next Start registers it again anyway — as native hsm_sensor_release frees only the
            // handle and keeps the sensor. So the description change applies and rides that Start.
            var sender = new RegistrationSender();
            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateIntSensor("describe/disposed-handle", Options("first"));
                await collector.Start().ConfigureAwait(false);
                Assert.True(await sender.WaitForRegistrationsAsync(1, WaitTimeout).ConfigureAwait(false));
                await collector.Stop().ConfigureAwait(false);

                ((IDisposable)sensor).Dispose();

                Assert.True(sensor.SetDescription("second"));
                await collector.Start().ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);

                Assert.Equal(2, sender.Registrations.Count);
                Assert.Equal("second", sender.Registrations[1].Description);
            }
        }

        private static SensorsStorage StorageOf(DataCollector collector) =>
            (SensorsStorage)typeof(DataCollector)
                .GetField("_sensorsStorage", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);

        [Fact]
        public async Task A_sensor_created_while_running_re_registers_after_its_own_registration()
        {
            var sender = new RegistrationSender();
            using (var collector = CreateCollector(sender))
            {
                await collector.Start().ConfigureAwait(false);
                var sensor = collector.CreateIntSensor("describe/runtime", Options("first"));
                Assert.True(sensor.SetDescription("second"));

                // Either one or two registrations may go out (the runtime create's init can run
                // before or after the change), but the server always ends with the new text.
                await collector.Stop().ConfigureAwait(false);
                Assert.InRange(sender.Registrations.Count, 1, 2);
                Assert.Equal("second", sender.Registrations.Last().Description);
            }
        }

        // --- Text handling ---

        [Fact]
        public async Task Null_is_sent_as_a_null_description_and_empty_as_empty()
        {
            var sender = new RegistrationSender();
            using (var collector = CreateCollector(sender))
            {
                var nullSensor = collector.CreateIntSensor("describe/null", Options("first"));
                var emptySensor = collector.CreateIntSensor("describe/empty", Options("first"));

                Assert.True(nullSensor.SetDescription(null));
                Assert.True(emptySensor.SetDescription(string.Empty));

                await collector.Start().ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);

                Assert.Null(sender.Registrations.Single(r => r.Path.EndsWith("describe/null")).Description);
                Assert.Equal(string.Empty, sender.Registrations.Single(r => r.Path.EndsWith("describe/empty")).Description);
            }
        }

        // --- Every sensor kind, via the public extension methods ---

        [Fact]
        public async Task Every_created_sensor_kind_accepts_a_new_description()
        {
            var sender = new RegistrationSender();
            using (var collector = CreateCollector(sender))
            {
                var inert = TimeSpan.FromDays(365);

                Assert.True(collector.CreateIntSensor("describe/kinds/instant").SetDescription("instant"));
                Assert.True(collector.CreateLastValueIntSensor("describe/kinds/last", 0).SetDescription("last"));
                Assert.True(collector.CreateRateSensor("describe/kinds/rate", new RateSensorOptions { PostDataPeriod = inert }).SetDescription("rate"));
                Assert.True(collector.CreateFileSensor("describe/kinds/file", "report").SetDescription("file"));
                Assert.True(collector.CreateIntBarSensor("describe/kinds/bar", new BarSensorOptions { PostDataPeriod = inert, BarTickPeriod = inert, BarPeriod = inert })
                    .SetDescription("bar"));
                Assert.True(collector.CreateFunctionSensor("describe/kinds/function", () => 1, new FunctionSensorOptions { PostDataPeriod = inert })
                    .SetDescription("function"));
                Assert.True(collector.CreateServiceCommandsSensor().SetDescription("commands"));

                await collector.Start().ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);

                foreach (var kind in new[] { "instant", "last", "rate", "file", "bar", "function" })
                    Assert.Equal(kind, sender.Registrations.Single(r => r.Path.EndsWith("describe/kinds/" + kind)).Description);

                Assert.Contains(sender.Registrations, r => r.Description == "commands");
            }
        }

        [Fact]
        public void A_null_handle_or_a_foreign_implementation_returns_false()
        {
            Assert.False(((IInstantValueSensor<int>)null).SetDescription("x"));
            Assert.False(((IBarSensor<int>)null).SetDescription("x"));
            Assert.False(((IServiceCommandsSensor)null).SetDescription("x"));
            Assert.False(((IBaseFuncSensor)null).SetDescription("x"));
            Assert.False(new ForeignInstantSensor().SetDescription("x"));
        }

        [Fact]
        public void The_capability_does_not_extend_the_public_sensor_interfaces()
        {
            // External implementations of the sensor interfaces (test doubles, adapters) must keep
            // compiling: the new call is a separate capability plus extension methods.
            foreach (var type in new[] { typeof(IInstantValueSensor<int>), typeof(IBarSensor<int>), typeof(IServiceCommandsSensor), typeof(IBaseFuncSensor), typeof(ISensor) })
                Assert.Empty(type.GetMethods().Where(m => m.Name == nameof(IDescribableSensor.SetDescription)));

            Assert.True(typeof(IDescribableSensor).IsAssignableFrom(typeof(SensorBase<NoDisplayUnit>)));
        }

        // --- Thread safety ---

        [Fact]
        public async Task Concurrent_calls_while_running_all_re_register_and_the_last_one_carries_the_final_text()
        {
            const int workers = 8;
            const int callsPerWorker = 50;

            var sender = new RegistrationSender();
            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateIntSensor("describe/concurrent", Options("first"));
                await collector.Start().ConfigureAwait(false);
                Assert.True(await sender.WaitForRegistrationsAsync(1, WaitTimeout).ConfigureAwait(false));

                using (var go = new ManualResetEventSlim(false))
                {
                    var tasks = Enumerable.Range(0, workers).Select(w => Task.Run(() =>
                    {
                        go.Wait();
                        var ok = true;
                        for (var i = 0; i < callsPerWorker; i++)
                            ok &= sensor.SetDescription($"w{w}-{i}");
                        return ok;
                    })).ToArray();

                    go.Set();
                    Assert.All(await Task.WhenAll(tasks).ConfigureAwait(false), Assert.True);
                }

                await collector.Stop().ConfigureAwait(false);

                // No re-registration lost, and the latest one queued carries the text the sensor ended with.
                Assert.Equal(1 + workers * callsPerWorker, sender.Registrations.Count);
                Assert.Equal(CurrentDescription(sensor), sender.Registrations.Last().Description);
            }
        }

        [Fact]
        public async Task Concurrent_calls_across_start_and_stop_never_throw_and_the_next_start_registers_the_latest_text()
        {
            var sender = new RegistrationSender();
            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateIntSensor("describe/lifecycle-race", Options("first"));

                using (var cancel = new CancellationTokenSource())
                {
                    var failures = 0;
                    var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
                    {
                        var i = 0;
                        while (!cancel.IsCancellationRequested)
                            if (!sensor.SetDescription($"w{w}-{i++}"))
                                Interlocked.Increment(ref failures);
                    })).ToArray();

                    for (var cycle = 0; cycle < 5; cycle++)
                    {
                        await collector.Start().ConfigureAwait(false);
                        await collector.Stop().ConfigureAwait(false);
                    }

                    cancel.Cancel();
                    await Task.WhenAll(writers).ConfigureAwait(false);
                    Assert.Equal(0, failures);
                }

                Assert.True(sensor.SetDescription("final"));
                await collector.Start().ConfigureAwait(false);
                await collector.Stop().ConfigureAwait(false);

                Assert.Equal("final", sender.Registrations.Last().Description);
            }
        }

        private static string CurrentDescription(object sensor)
        {
            var field = typeof(SensorBase<NoDisplayUnit>).GetField("_metainfo", BindingFlags.Instance | BindingFlags.NonPublic);
            return ((SensorOptions)field.GetValue(sensor)).Description;
        }

        private sealed class ForeignInstantSensor : IInstantValueSensor<int>
        {
            public void AddValue(int value) { }

            public void AddValue(int value, string comment = "") { }

            public void AddValue(int value, SensorStatus status = SensorStatus.Ok, string comment = "") { }
        }

        private sealed class RegistrationSender : IDataSender
        {
            private readonly object _lock = new object();
            private readonly List<AddOrUpdateSensorRequest> _registrations = new List<AddOrUpdateSensorRequest>();

            public IReadOnlyList<AddOrUpdateSensorRequest> Registrations
            {
                get
                {
                    lock (_lock)
                        return _registrations.ToList();
                }
            }

            public async Task<bool> WaitForRegistrationsAsync(int count, TimeSpan timeout)
            {
                var stopAt = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < stopAt)
                {
                    if (Registrations.Count >= count)
                        return true;

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return Registrations.Count >= count;
            }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token)
            {
                lock (_lock)
                    _registrations.AddRange(commands.OfType<AddOrUpdateSensorRequest>());

                return default;
            }

            public void Dispose() { }
        }
    }
}
