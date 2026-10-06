using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using System;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// The ownership mark SetDescription relies on (#1482) and the order SensorsStorage sets it in
    /// (#1508): a sensor is marked owned BEFORE it becomes visible in storage, so a removal of the
    /// same path that interleaves with the add can never leave a removed sensor marked owned.
    /// </summary>
    public sealed class SensorsStorageOwnershipTests
    {
        private const string Path = "ownership/racing";

        [Fact]
        public void A_sensor_is_marked_owned_before_it_is_visible_in_storage()
        {
            using (var collector = CreateCollector())
            {
                var storage = StorageOf(collector);
                var sensor = new RecordingSensor(Path);

                sensor.OnMarkOwned = () => sensor.VisibleWhenMarked = storage.TryGetValue(Path, out _);

                Assert.Same(sensor, storage.Register(sensor));

                Assert.False(sensor.VisibleWhenMarked, "ownership must be marked before TryAdd publishes the sensor");
                Assert.True(sensor.Owned);
            }
        }

        [Fact]
        public void A_removal_racing_the_add_never_leaves_a_removed_sensor_owned()
        {
            using (var collector = CreateCollector())
            {
                var storage = StorageOf(collector);
                var sensor = new RecordingSensor(Path);

                // The concurrent TryRemove of the same path, run at the point where the mark is set.
                // Marked after TryAdd (the old order) it removed the sensor and released it, and the
                // mark then made the removed instance owned again: a ghost SetDescription registers.
                sensor.OnMarkOwned = () => storage.TryRemove(Path, out _);

                storage.Register(sensor);

                var stored = storage.TryGetValue(Path, out var current) && ReferenceEquals(current, sensor);
                Assert.Equal(stored, sensor.Owned);
            }
        }

        private static DataCollector CreateCollector() =>
            new DataCollector(new CollectorOptions
            {
                AccessKey = "ownership-key",
                ClientName = "ownership-client",
                ComputerName = "ownership-host",
                Module = "ownership-module",
            });

        private static SensorsStorage StorageOf(DataCollector collector) =>
            (SensorsStorage)typeof(DataCollector)
                .GetField("_sensorsStorage", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);

        private sealed class RecordingSensor : ISensor, ICollectorOwnedSensor
        {
            internal RecordingSensor(string path) => SensorPath = path;

            public string SensorPath { get; }

            internal Action OnMarkOwned { get; set; }

            internal bool Owned { get; private set; }

            internal bool VisibleWhenMarked { get; set; }

            public void MarkOwned()
            {
                OnMarkOwned?.Invoke();
                Owned = true;
            }

            public void MarkReleased() => Owned = false;

            public ValueTask<bool> InitAsync() => new ValueTask<bool>(true);

            public ValueTask<bool> StartAsync() => new ValueTask<bool>(true);

            public ValueTask StopAsync() => default;

            public void Dispose() { }
        }
    }
}
