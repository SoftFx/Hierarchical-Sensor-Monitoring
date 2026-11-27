using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.Formatters;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Managers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using NLog;
using NLog.Config;
using NLog.Targets;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests
{
    // #1925: delivery is at-least-once (ADR-0017), so a collector may send a frame it has already delivered, e.g. the
    // frame whose ack a stop cut off goes again in the stop drain. The server takes a value whose time and content
    // equal a value it already stored for that sensor as a no-op: no row, no alert, no EMA, no aggregate count, no
    // out-of-order or same-tick count and no Warn line. Every case sends the frame [28 @ t28, 29 @ t29] twice.
    public class RepeatedDeliveryTests : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        private static readonly MemoryPackFormatter _formatter = new();

        private readonly TemplateConcurrencyFixture _fixture;


        public RepeatedDeliveryTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        [Fact]
        public async Task A_repeated_frame_stores_nothing_and_counts_nothing()
        {
            var path = "repeatedDelivery/instant";
            var t28 = StorageWeekTestTime.WithinOneWeek(DateTime.UtcNow.AddMinutes(-30), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            var t29 = t28.AddSeconds(1);

            using var warns = new WarnCapture(path);

            await SendAsync(Frame(path, t28, 28, t29, 29));
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));
            var rowsFirst = await ReadRowsAsync(sensor.Id);

            await SendAsync(Frame(path, t28, 28, t29, 29));

            var rowsRepeat = await ReadRowsAsync(sensor.Id);

            Assert.Equal(new[] { t28, t29 }, rowsRepeat.Select(v => v.Time));
            Assert.Equal(new[] { 28.0, 29.0 }, rowsRepeat.Cast<DoubleValue>().Select(v => v.Value));
            Assert.Equal(rowsFirst.Select(v => v.ReceivingTime), rowsRepeat.Select(v => v.ReceivingTime));
            Assert.Equal(0, Volatile.Read(ref sensor.OutOfOrderValuesStored));
            Assert.Equal(0, Volatile.Read(ref sensor.SameTickValuesSuperseded));
            Assert.Equal(0, Volatile.Read(ref sensor.RejectedValues));
            Assert.Empty(warns.Lines);
            Assert.Equal(t29, sensor.LastValue.Time);
        }

        [Fact]
        public async Task A_repeated_frame_after_newer_values_stores_nothing()
        {
            var path = "repeatedDelivery/afterNewer";
            var t28 = StorageWeekTestTime.WithinOneWeek(DateTime.UtcNow.AddMinutes(-30), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            var t29 = t28.AddSeconds(1);
            var t30 = t28.AddSeconds(2);

            using var warns = new WarnCapture(path);

            // The stop drain sends what was queued after the frame first, then the frame again.
            await SendAsync(Frame(path, t28, 28, t29, 29));
            await SendAsync([Value(path, t30, 30)]);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            await SendAsync(Frame(path, t28, 28, t29, 29));

            Assert.Equal(new[] { t28, t29, t30 }, (await ReadRowsAsync(sensor.Id)).Select(v => v.Time));
            Assert.Equal(0, Volatile.Read(ref sensor.OutOfOrderValuesStored));
            Assert.Empty(warns.Lines);
            Assert.Equal(t30, sensor.LastValue.Time);
        }

        [Fact]
        public async Task A_value_on_the_same_tick_with_other_content_still_replaces_the_stored_one()
        {
            var path = "repeatedDelivery/sameTickOtherContent";
            var t28 = StorageWeekTestTime.WithinOneWeek(DateTime.UtcNow.AddMinutes(-30), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            var t29 = t28.AddSeconds(1);

            await SendAsync(Frame(path, t28, 28, t29, 29));
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            // Another value, a comment or a status on a stored tick is not a repeat (another client, a correction).
            await SendAsync([Value(path, t28, 128), Value(path, t29, 29, "corrected")]);

            var rows = await ReadRowsAsync(sensor.Id);

            Assert.Equal(new[] { 128.0, 29.0 }, rows.Cast<DoubleValue>().Select(v => v.Value));
            Assert.Equal("corrected", rows[1].Comment);
            Assert.Equal("corrected", sensor.LastValue.Comment);
            Assert.Equal(1, Volatile.Read(ref sensor.OutOfOrderValuesStored));
            Assert.Equal(1, Volatile.Read(ref sensor.SameTickValuesSuperseded));
        }

        [Fact]
        public async Task A_repeated_frame_raises_no_alert_again()
        {
            var path = "repeatedDelivery/alert";
            var t27 = DateTime.UtcNow.AddMinutes(-30);
            var t28 = t27.AddSeconds(1);
            var t29 = t27.AddSeconds(2);

            await SendAsync([Value(path, t27, 27)]);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            var update = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = InitiatorInfo.AsSystemForce("tests"),
                Policies =
                [
                    new PolicyUpdate
                    {
                        Id = Guid.NewGuid(),
                        Template = "value above zero",
                        Destination = new PolicyDestinationUpdate(PolicyDestinationMode.FromParent),
                        Conditions =
                        [
                            new PolicyConditionUpdate
                            {
                                Operation = PolicyOperation.GreaterThan,
                                Property = PolicyProperty.Value,
                                Target = new TargetValue(TargetType.Const, "0"),
                            },
                        ],
                    },
                ],
            });
            Assert.True(update.IsOk, update.Error);

            var policy = Assert.Single(sensor.Policies);
            var sent = new ConcurrentQueue<AlertResult>();

            void Record(AlertMessage message)
            {
                if (message.SensorId == sensor.Id)
                    foreach (var alert in message)
                        sent.Enqueue(alert);
            }

            _valuesCache.NewAlertMessageEvent += Record;

            try
            {
                await SendAsync(Frame(path, t28, 28, t29, 29));
                Assert.Equal(2, sent.Count(a => a.PolicyId == policy.Id));

                await SendAsync(Frame(path, t28, 28, t29, 29));
                Assert.Equal(2, sent.Count(a => a.PolicyId == policy.Id));
            }
            finally
            {
                _valuesCache.NewAlertMessageEvent -= Record;
            }
        }

        [Fact]
        public async Task A_repeated_frame_leaves_the_ema_as_it_was()
        {
            var path = "repeatedDelivery/ema";
            var t27 = DateTime.UtcNow.AddMinutes(-30);
            var t28 = t27.AddSeconds(1);
            var t29 = t27.AddSeconds(2);

            await SendAsync([Value(path, t27, 27)]);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            var update = await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, Statistics = StatisticsOptions.EMA, Initiator = InitiatorInfo.System });
            Assert.True(update.IsOk, update.Error);

            await SendAsync(Frame(path, t28, 28, t29, 29));

            var emaFirst = (await ReadRowsAsync(sensor.Id)).Cast<DoubleValue>().Select(v => v.EmaValue).ToList();
            var lastFirst = ((DoubleValue)sensor.LastValue).EmaValue;

            await SendAsync(Frame(path, t28, 28, t29, 29));

            Assert.Equal(emaFirst, (await ReadRowsAsync(sensor.Id)).Cast<DoubleValue>().Select(v => v.EmaValue));
            Assert.Equal(lastFirst, ((DoubleValue)sensor.LastValue).EmaValue);
        }

        [Fact]
        public async Task A_repeated_frame_adds_nothing_to_an_aggregated_value()
        {
            var path = "repeatedDelivery/aggregate";
            var t27 = StorageWeekTestTime.WithinOneWeek(DateTime.UtcNow.AddMinutes(-30), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            var t28 = t27.AddSeconds(1);
            var t29 = t27.AddSeconds(2);

            using var warns = new WarnCapture(path);

            await SendAsync([Value(path, t27, 27)]);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            var update = await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, AggregateValues = true, Initiator = InitiatorInfo.System });
            Assert.True(update.IsOk, update.Error);

            await SendAsync(Frame(path, t28, 28, t29, 29));
            await SendAsync(Frame(path, t28, 28, t29, 29));

            var rows = await ReadRowsAsync(sensor.Id);

            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => Assert.Equal(1, r.AggregatedValuesCount));
            Assert.Equal(1, sensor.LastValue.AggregatedValuesCount);
            Assert.Equal(0, Volatile.Read(ref sensor.AggregateSpanOverlapsSkipped));
            Assert.Empty(warns.Lines);
        }

        [Fact]
        public async Task A_value_equal_to_an_aggregated_run_within_its_span_is_a_repeat()
        {
            var path = "repeatedDelivery/aggregateRun";
            var t28 = StorageWeekTestTime.WithinOneWeek(DateTime.UtcNow.AddMinutes(-30), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            var t29 = t28.AddSeconds(1);

            await SendAsync([Value(path, t28.AddSeconds(-1), 27)]);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            var update = await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, AggregateValues = true, Initiator = InitiatorInfo.System });
            Assert.True(update.IsOk, update.Error);

            // 5 at t28, t29 and t30 are one run (count 3, last update t30); the frame [t28, t29] comes again, after t30.
            await SendAsync(Frame(path, t28, 5, t29, 5));
            await SendAsync([Value(path, t29.AddSeconds(1), 5)]);
            await SendAsync(Frame(path, t28, 5, t29, 5));

            Assert.Equal(3, sensor.LastValue.AggregatedValuesCount);
            Assert.Equal(t29.AddSeconds(1), sensor.LastValue.LastUpdateTime);
            Assert.Equal(0, Volatile.Read(ref sensor.AggregateSpanOverlapsSkipped));
        }

        [Fact]
        public async Task A_value_equal_to_a_stored_run_is_a_repeat_after_aggregation_is_switched_off()
        {
            var path = "repeatedDelivery/aggregateOff";
            var t28 = StorageWeekTestTime.WithinOneWeek(DateTime.UtcNow.AddMinutes(-30), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            var t29 = t28.AddSeconds(1);
            var t30 = t28.AddSeconds(2);

            using var warns = new WarnCapture(path);

            await SendAsync([Value(path, t28.AddSeconds(-1), 27)]);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, AggregateValues = true, Initiator = InitiatorInfo.System })).IsOk);

            // One run 5 @ t28..t30, then a newer value, then aggregation goes off; the frame [t29] comes again.
            await SendAsync(Frame(path, t28, 5, t29, 5));
            await SendAsync([Value(path, t30, 5), Value(path, t30.AddSeconds(1), 6)]);

            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, AggregateValues = false, Initiator = InitiatorInfo.System })).IsOk);

            await SendAsync([Value(path, t29, 5)]);

            var rows = await ReadRowsAsync(sensor.Id);

            Assert.DoesNotContain(rows, r => r.Time == t29);
            Assert.Equal(3, rows.Single(r => r.Time == t28).AggregatedValuesCount);
            Assert.Equal(0, Volatile.Read(ref sensor.OutOfOrderValuesStored));
            Assert.Empty(warns.Lines);
        }

        [Fact]
        public async Task A_repeated_frame_of_bars_keeps_the_bar_in_progress()
        {
            var path = "repeatedDelivery/bars";
            var t27 = DateTime.UtcNow.AddMinutes(-30);
            var t28 = t27.AddMinutes(1);
            var t29 = t27.AddMinutes(2);
            var t30 = t27.AddMinutes(3);

            await SendAsync([Bar(path, t27, t28, 27)]);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            SensorValueBase[] frame = [Bar(path, t28, t29, 28), Bar(path, t29, t30, 29)];

            await SendAsync(frame);
            var rowsFirst = await ReadRowsAsync(sensor.Id);
            Assert.Equal(t29, ((DoubleBarValue)sensor.LastValue).OpenTime);

            // Value by value: the repeated bar 28 must not close the bar 29 in progress and take its place.
            await SendAsync([frame[0]]);
            Assert.Equal(t29, ((DoubleBarValue)sensor.LastValue).OpenTime);

            await SendAsync([frame[1]]);

            Assert.Equal(rowsFirst.Select(r => r.Time), (await ReadRowsAsync(sensor.Id)).Select(r => r.Time));
            Assert.Equal(t29, ((DoubleBarValue)sensor.LastValue).OpenTime);
            Assert.Equal(29, ((DoubleBarValue)sensor.LastValue).Mean);
        }

        [Fact]
        public async Task A_bar_partial_on_the_same_tick_with_other_content_still_applies()
        {
            var path = "repeatedDelivery/barPartial";
            var t27 = DateTime.UtcNow.AddMinutes(-30);
            var t28 = t27.AddMinutes(1);
            var t29 = t27.AddMinutes(2);

            await SendAsync([Bar(path, t27, t28, 27)]);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            await SendAsync([Bar(path, t28, t29, 28)]);
            await SendAsync([Bar(path, t28, t29, 28.5)]);

            Assert.Equal(28.5, ((DoubleBarValue)sensor.LastValue).Mean);
        }


        private async Task SendAsync(IEnumerable<SensorValueBase> values)
        {
            var response = await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, values);
            Assert.Empty(response);
        }

        private static SensorValueBase[] Frame(string path, DateTime t1, double v1, DateTime t2, double v2) =>
            [Value(path, t1, v1), Value(path, t2, v2)];

        private static DoubleSensorValue Value(string path, DateTime time, double value, string comment = null) => new()
        {
            Path = path,
            Time = time,
            Value = value,
            Comment = comment,
            Status = HSMSensorDataObjects.SensorStatus.Ok,
        };

        private static DoubleBarSensorValue Bar(string path, DateTime open, DateTime close, double mean) => new()
        {
            Path = path,
            Time = close,
            OpenTime = open,
            CloseTime = close,
            Min = mean - 1,
            Max = mean + 1,
            Mean = mean,
            Count = 3,
            Status = HSMSensorDataObjects.SensorStatus.Ok,
        };

        private async Task<List<BaseValue>> ReadRowsAsync(Guid sensorId)
        {
            var values = new List<BaseValue>();

            await foreach (var bytes in _databaseCoreManager.DatabaseCore.GetSensorValues(sensorId, DateTime.MinValue, DateTime.MaxValue))
                values.Add(_formatter.Deserialize(bytes));

            return values.OrderBy(v => v.Time).ToList();
        }

        // A Warn+ rule for the TreeValuesCache logger beside whatever is configured; keeps the lines about this
        // test's sensor.
        private sealed class WarnCapture : IDisposable
        {
            private readonly string _path;
            private readonly MemoryTarget _memory = new() { Layout = "${message}" };
            private readonly LoggingRule _rule;
            private readonly LoggingConfiguration _config;

            public WarnCapture(string path)
            {
                _path = path;
                _config = LogManager.Configuration ?? new LoggingConfiguration();
                _rule = new LoggingRule("TreeValuesCache", NLog.LogLevel.Warn, _memory);
                _config.LoggingRules.Add(_rule);

                if (LogManager.Configuration is null)
                    LogManager.Configuration = _config;
                else
                    LogManager.ReconfigExistingLoggers();
            }

            public List<string> Lines => _memory.Logs.Where(l => l.Contains($"'{_path}'")).ToList();

            public void Dispose()
            {
                _config.LoggingRules.Remove(_rule);
                LogManager.ReconfigExistingLoggers();
            }
        }
    }
}
