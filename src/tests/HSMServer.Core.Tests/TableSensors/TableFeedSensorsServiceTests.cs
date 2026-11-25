using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.TableBlocks;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.TableSensors;
using HSMServer.ServerConfiguration;
using HSMServer.ServiceExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using Xunit;
using ApiValues = HSMSensorDataObjects.SensorValueRequests;

namespace HSMServer.Core.Tests.TableSensors
{
    /// <summary>
    /// The feed sensors' service and counters without a database (#1847): lanes per product, the bound of a lane, the 0
    /// kept pending across a failed write, clamped counts and once-per-path logs, the counters under concurrent puts and
    /// the container's disposal. The values go to a recording <see cref="IServerSensorValues"/>; nothing waits on time:
    /// a slow product is a gate the test opens, concurrency is a barrier.
    /// </summary>
    [Collection("NLog isolated")]
    public sealed class TableFeedSensorsServiceTests : IDisposable
    {
        private static readonly DateTime T0 = new(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);

        private readonly MemoryTarget _memory = new() { Layout = "${level} ${message}" };
        private readonly LoggingConfiguration _previous = LogManager.Configuration;

        private readonly Guid _a = Guid.NewGuid(), _b = Guid.NewGuid();
        private readonly TableFeedCounters _counters = new();
        private readonly RecordingValues _values = new();
        private readonly Mock<ITreeValuesCache> _cache = new();


        public TableFeedSensorsServiceTests()
        {
            LogManager.Setup().LoadConfiguration(cfg => cfg.ForLogger().WriteTo(_memory));

            ProductModel product = null;
            string message = null;
            _cache.Setup(c => c.TryGetRootProduct(It.IsAny<Guid>(), out product, out message)).Returns(true);
        }

        public void Dispose() => LogManager.Configuration = _previous;


        [Fact]
        public async Task A_slow_product_delays_only_itself_and_every_tick_drains_its_own_period()
        {
            var feed = Feed();
            var gate = _values.Gate(_a);

            Block(_a, 1);
            Block(_b, 10);
            var first = feed.TickAsync(T0.AddMinutes(1));

            Block(_a, 2);
            Block(_b, 20);
            var second = feed.TickAsync(T0.AddMinutes(2));

            // B posted both periods apart while A's queue holds its first.
            await feed.WhenPosted(_b);
            Assert.Equal([(T0.AddMinutes(1), 10), (T0.AddMinutes(2), 20)], _values.Rows(_b));
            Assert.False(first.IsCompleted);

            gate.SetResult();
            await Task.WhenAll(first, second);
            Assert.Equal([(T0.AddMinutes(1), 1), (T0.AddMinutes(2), 2)], _values.Rows(_a));
        }

        [Fact]
        public async Task A_product_whose_queue_does_not_answer_keeps_at_most_the_bound_and_says_so_once()
        {
            var feed = Feed();
            var gate = _values.Gate(_a);
            var ticks = new List<Task>();

            // One period in flight, MaxPendingPeriods waiting, two more: the two oldest waiting are dropped.
            for (int minute = 1; minute <= TableFeedSensorsService.MaxPendingPeriods + 3; minute++)
            {
                Block(_a, minute);
                ticks.Add(feed.TickAsync(T0.AddMinutes(minute)));
            }

            Assert.Single(Logs("Error"), l => l.Contains($"product {_a}") && l.Contains("dropped"));

            gate.SetResult();
            await Task.WhenAll(ticks);

            var posted = _values.Rows(_a).Select(r => r.Value).ToList();
            Assert.Equal(TableFeedSensorsService.MaxPendingPeriods + 1, posted.Count);
            Assert.Equal([1, .. Enumerable.Range(4, TableFeedSensorsService.MaxPendingPeriods)], posted);
        }

        [Fact]
        public async Task A_failed_write_of_the_0_keeps_it_pending_for_both_failure_counters()
        {
            var feed = Feed();

            _counters.AddRefused(_a, 3);
            _counters.AddUnreadable(_a, 2);
            await feed.TickAsync(T0.AddMinutes(1));

            _values.ThrowNext = true;
            await feed.TickAsync(T0.AddMinutes(2)); // the zeros fail
            await feed.TickAsync(T0.AddMinutes(3)); // and are sent again
            await feed.TickAsync(T0.AddMinutes(4)); // accepted before: silent

            foreach (var name in new[] { TableFeedSensorsService.RefusedName, TableFeedSensorsService.UnreadableName })
            {
                var posted = _values.Of(_a, $".tables/{name}");
                Assert.Equal([T0.AddMinutes(1), T0.AddMinutes(3)], posted.Select(v => v.Time));
                Assert.Equal(0, ((ApiValues.IntSensorValue)posted[^1]).Value);
            }

            // A refused 0 is kept too: the next period sends it again.
            _counters.AddRefused(_a, 1);
            await feed.TickAsync(T0.AddMinutes(5));
            _values.Refuse = true;
            await feed.TickAsync(T0.AddMinutes(6));
            _values.Refuse = false;
            await feed.TickAsync(T0.AddMinutes(7));
            await feed.TickAsync(T0.AddMinutes(8));

            Assert.Equal([T0.AddMinutes(5), T0.AddMinutes(6), T0.AddMinutes(7)], _values.Of(_a, ".tables/Tables refused").Skip(2).Select(v => v.Time));
        }

        [Fact]
        public async Task A_count_beyond_the_integer_range_is_posted_clamped_and_logged_once_with_its_number()
        {
            var feed = Feed();
            var sensor = Guid.NewGuid();

            for (int minute = 1; minute <= 2; minute++)
            {
                _counters.AddStored(_a, [(sensor, int.MaxValue), (sensor, int.MaxValue)], 1, 0, T0.Ticks, T0.Ticks);
                await feed.TickAsync(T0.AddMinutes(minute));
            }

            Assert.All(_values.Of(_a, ".tables/Rows"), v => Assert.Equal(int.MaxValue, ((ApiValues.IntSensorValue)v).Value));
            var error = Assert.Single(Logs("Error"));
            Assert.Contains($"{2L * int.MaxValue}", error);
            Assert.Contains(".tables/Rows", error);
        }

        [Fact]
        public async Task A_refused_path_is_logged_once_not_every_tick()
        {
            var feed = Feed();
            _values.RefusePath = ".tables/Rows";

            for (int minute = 1; minute <= 3; minute++)
            {
                Block(_a, 1);
                await feed.TickAsync(T0.AddMinutes(minute));
            }

            Assert.Single(Logs("Warn"), l => l.Contains("'.tables/Rows'"));
        }

        [Fact]
        public void Concurrent_puts_and_drains_lose_nothing_and_count_nothing_twice_and_a_block_is_in_one_period_for_both_rows()
        {
            const int perThread = 20_000;
            var sensor = Guid.NewGuid();
            var start = new Barrier(3);
            var done = 0;

            void Puts()
            {
                start.SignalAndWait();
                for (int i = 0; i < perThread; i++)
                    _counters.AddStored(_a, [(sensor, 1)], 1, 0, 0, 0);
                Interlocked.Increment(ref done);
            }

            var threads = new[] { new Thread(Puts), new Thread(Puts) };
            foreach (var thread in threads)
                thread.Start();

            long blocks = 0, productRows = 0, tableRows = 0;
            start.SignalAndWait();

            while (true)
            {
                var finished = Volatile.Read(ref done) == 2;
                var (products, tables) = _counters.Drain(tables: true);
                var period = products.GetValueOrDefault(_a);
                var rows = tables.GetValueOrDefault(sensor);

                // One acquisition: the product's and the table's rows of a drain are the same blocks.
                Assert.Equal(period?.Rows ?? 0, rows);

                blocks += period?.Blocks ?? 0;
                productRows += period?.Rows ?? 0;
                tableRows += rows;

                if (finished)
                    break;
            }

            foreach (var thread in threads)
                thread.Join();

            Assert.Equal((2L * perThread, 2L * perThread, 2L * perThread), (blocks, productRows, tableRows));
        }

        [Fact]
        public async Task The_container_disposes_the_cache_once_with_the_feed_registered()
        {
            var disposals = 0;
            var cache = new Mock<ITreeValuesCache>();
            cache.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Callback(() => disposals++).Returns(ValueTask.CompletedTask);

            var store = new Mock<ITableBlockStore>();
            var database = new Mock<IDatabaseCore>();
            database.SetupGet(d => d.TableBlocks).Returns(store.Object);

            var config = new Mock<IServerConfig>();
            config.SetupGet(c => c.TableSensors).Returns(new TableSensorsConfig());

            var services = new ServiceCollection();
            services.AddSingleton(config.Object);
            services.AddSingleton(_ => cache.Object); // created by the container, as the app's cache is
            services.AddSingleton(provider => new TableBlockService(database.Object, provider.GetRequiredService<ITreeValuesCache>()));
            services.AddTableFeedSensors();

            var provider = services.BuildServiceProvider(validateScopes: true);
            Assert.Single(provider.GetServices<IHostedService>().OfType<TableFeedSensorsService>());
            Assert.IsType<ServerSensorValues>(provider.GetRequiredService<IServerSensorValues>());

            await provider.DisposeAsync();

            Assert.Equal(1, disposals);
        }


        // === Helpers ===

        private TableFeedSensorsService Feed() => new(_counters, _cache.Object, _values, new TableSensorsConfig { FeedTableSensors = false });

        private void Block(Guid product, int rows) => _counters.AddStored(product, [(Guid.NewGuid(), rows)], 1, 0, T0.Ticks, T0.Ticks);

        private List<string> Logs(string level)
        {
            LogManager.Flush();
            return [.. _memory.Logs.Where(l => l.StartsWith(level))];
        }


        // Records what reached each product; a product's calls can wait on a gate, the next call can throw or be refused.
        private sealed class RecordingValues : IServerSensorValues
        {
            private readonly object _lock = new();
            private readonly Dictionary<Guid, List<ApiValues.SensorValueBase>> _posted = [];
            private readonly Dictionary<Guid, TaskCompletionSource> _gates = [];

            public bool ThrowNext { get; set; }

            public bool Refuse { get; set; }

            public string RefusePath { get; set; }


            public TaskCompletionSource Gate(Guid product)
            {
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_lock)
                    _gates[product] = gate;
                return gate;
            }

            public async Task<Dictionary<string, string>> AddServerValuesAsync(Guid productId, IReadOnlyList<ServerSensorValue> values, CancellationToken token = default)
            {
                TaskCompletionSource gate;
                lock (_lock)
                    _gates.TryGetValue(productId, out gate);

                if (gate is not null)
                    await gate.Task;

                if (ThrowNext)
                {
                    ThrowNext = false;
                    throw new InvalidOperationException("The queue failed.");
                }

                var answer = new Dictionary<string, string>();

                lock (_lock)
                {
                    if (!_posted.TryGetValue(productId, out var list))
                        _posted[productId] = list = [];

                    foreach (var value in values)
                    {
                        list.Add(value.Value);

                        if (Refuse || value.Value.Path == RefusePath)
                            answer[value.Value.Path] = "refused";
                    }
                }

                return answer;
            }

            public List<ApiValues.SensorValueBase> Of(Guid product, string path)
            {
                lock (_lock)
                    return _posted.TryGetValue(product, out var list) ? list.Where(v => v.Path == path).ToList() : [];
            }

            public List<(DateTime Time, int Value)> Rows(Guid product) =>
                Of(product, ".tables/Rows").Select(v => (v.Time, ((ApiValues.IntSensorValue)v).Value)).ToList();
        }
    }
}
