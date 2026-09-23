using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using HSMDataCollector.Client;
using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1102-E4: a busy keep-alive connection never re-resolves DNS — when a load balancer moves but
    /// the old IP still accepts TCP, the agent sticks to it indefinitely. On .NET Framework the fix
    /// is a bounded ServicePoint.ConnectionLeaseTimeout for the collector endpoint (net6+ uses
    /// SocketsHttpHandler.PooledConnectionLifetime, compiled per-TFM).
    ///
    /// #1102-E5: SensorValueBase.Time has a public setter that accepts DateTimeKind.Local; such
    /// values serialize with a machine-local offset and shift timestamp interpretation. The collector
    /// normalizes Local times to UTC at the SendValue boundary (the wire DTO stays untouched).
    /// </summary>
    public sealed class TransportAndTimeNormalizationTests
    {
        [Fact]
        public void Https_client_bounds_connection_lease_for_dns_reresolution()
        {
            var options = new CollectorOptions
            {
                ServerAddress = "https://dns-staleness-test.local",
                AccessKey = "transport-test-key",
                ClientName = "transport-test-client",
            };

            using (var client = new HsmHttpsClient(options, new LoggerManager()))
            {
                var servicePoint = ServicePointManager.FindServicePoint(new Uri("https://dns-staleness-test.local:44330/api/sensors"));

                Assert.True(servicePoint.ConnectionLeaseTimeout > 0,
                    "ConnectionLeaseTimeout must be bounded so a keep-alive connection periodically re-resolves DNS.");
            }
        }

        [Fact]
        public void Local_time_is_normalized_to_utc_at_send()
        {
            using (var collector = new DataCollector(CreateOptions()))
            {
                var sensor = collector.CreateDoubleSensor("normalization/local-time/data");
                var localTime = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Local);
                var value = new DoubleSensorValue { Value = 1.0, Time = localTime };

                ((SensorBase<NoDisplayUnit>)sensor).SendValue(value);

                Assert.Equal(DateTimeKind.Utc, value.Time.Kind);
                Assert.Equal(localTime.ToUniversalTime(), value.Time);
            }
        }

        [Fact]
        public void Utc_time_is_left_untouched_at_send()
        {
            using (var collector = new DataCollector(CreateOptions()))
            {
                var sensor = collector.CreateDoubleSensor("normalization/utc-time/data");
                var utcTime = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
                var value = new DoubleSensorValue { Value = 1.0, Time = utcTime };

                ((SensorBase<NoDisplayUnit>)sensor).SendValue(value);

                Assert.Equal(utcTime, value.Time);
                Assert.Equal(DateTimeKind.Utc, value.Time.Kind);
            }
        }

        [Fact]
        public async Task Local_time_reaches_the_sender_as_utc_end_to_end()
        {
            var sender = new RecordingDataSender();
            var options = CreateOptions();
            options.DataSender = sender;
            options.PackageCollectPeriod = TimeSpan.FromMilliseconds(50);

            using (var collector = new DataCollector(options))
            {
                var sensor = collector.CreateDoubleSensor("normalization/pipeline/data");
                await collector.Start().ConfigureAwait(false);

                var localTime = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Local);
                ((SensorBase<NoDisplayUnit>)sensor).SendValue(new DoubleSensorValue { Value = 1.0, Time = localTime });

                var captured = await sender.WaitForFirstValueAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                Assert.NotNull(captured);
                Assert.Equal(DateTimeKind.Utc, captured.Time.Kind);
                Assert.Equal(localTime.ToUniversalTime(), captured.Time);

                await collector.Stop().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// #1433: the ".module" version markers carry a wire-visible "Start:"/"Stop:" timestamp that
        /// must read identically on every host. "dd/MM/yyyy HH:mm:ss" is a CUSTOM format string, in
        /// which '/' and ':' are the date/time SEPARATOR placeholders rather than literals — with the
        /// current culture applied, ru-RU renders "22.09.2026 18:33:37" and the native collector,
        /// which hard-codes '/' and ':', stops matching. This test forces a culture whose separators
        /// differ from the invariant ones and pins the rendered layout.
        /// </summary>
        [Theory]
        [InlineData("ru-RU")]
        [InlineData("de-DE")]
        public async Task Version_marker_comments_are_culture_invariant(string cultureName)
        {
            var sender = new RecordingDataSender();
            var options = CreateOptions();
            options.DataSender = sender;
            options.PackageCollectPeriod = TimeSpan.FromMilliseconds(50);

            var culture = CultureInfo.GetCultureInfo(cultureName);

            // Sanity: the culture really does disagree with the invariant separators, otherwise the
            // test would pass without exercising anything (e.g. under InvariantGlobalization).
            Assert.NotEqual("/", culture.DateTimeFormat.DateSeparator);

            var previousCulture = CultureInfo.CurrentCulture;
            var previousUiCulture = CultureInfo.CurrentUICulture;

            try
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;

                using (var collector = new DataCollector(options))
                {
                    if (DataCollector.IsWindowsOS)
                        collector.Windows.AddCollectorVersion();
                    else
                        collector.Unix.AddCollectorVersion();

                    await collector.Start().ConfigureAwait(false);
                    await collector.Stop().ConfigureAwait(false);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                CultureInfo.CurrentUICulture = previousUiCulture;
            }

            var comments = sender.Comments;

            Assert.Contains(comments, comment => IsMarker(comment, "Start"));
            Assert.Contains(comments, comment => IsMarker(comment, "Stop"));
            Assert.All(
                comments,
                comment => Assert.DoesNotContain(culture.DateTimeFormat.DateSeparator, comment));
        }

        // "<prefix>: dd/MM/yyyy HH:mm:ss" with the invariant separators, digits everywhere else.
        private static bool IsMarker(string comment, string prefix)
        {
            const string layout = "dd/MM/yyyy HH:mm:ss";
            var head = prefix + ": ";

            if (comment == null || comment.Length != head.Length + layout.Length ||
                !comment.StartsWith(head, StringComparison.Ordinal))
                return false;

            for (var i = 0; i < layout.Length; i++)
            {
                var actual = comment[head.Length + i];
                var digitExpected = layout[i] != '/' && layout[i] != ':' && layout[i] != ' ';

                if (digitExpected ? !char.IsDigit(actual) : actual != layout[i])
                    return false;
            }

            return true;
        }


        private static CollectorOptions CreateOptions() => new CollectorOptions
        {
            AccessKey = "normalization-test-key",
            ClientName = "normalization-test-client",
            ComputerName = "normalization-test-host",
            Module = "normalization-test-module",
            ServerAddress = "https://127.0.0.1",
        };


        private sealed class RecordingDataSender : IDataSender
        {
            private readonly System.Collections.Generic.List<SensorValueBase> _values = new System.Collections.Generic.List<SensorValueBase>();
            private readonly System.Threading.Tasks.TaskCompletionSource<bool> _valueReceived = new System.Threading.Tasks.TaskCompletionSource<bool>();

            // Every captured comment, in delivery order. Snapshotted under the lock because the
            // queue processor appends from its own thread.
            internal System.Collections.Generic.IReadOnlyList<string> Comments
            {
                get
                {
                    lock (_values)
                        return _values.Select(value => value.Comment ?? string.Empty).ToList();
                }
            }

            internal async System.Threading.Tasks.Task<SensorValueBase> WaitForFirstValueAsync(TimeSpan timeout)
            {
                await System.Threading.Tasks.Task.WhenAny(_valueReceived.Task, System.Threading.Tasks.Task.Delay(timeout)).ConfigureAwait(false);

                lock (_values)
                    return _values.Count > 0 ? _values[0] : null;
            }

            public void Dispose() { }

            public System.Threading.Tasks.ValueTask<ConnectionResult> TestConnectionAsync() =>
                new System.Threading.Tasks.ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public System.Threading.Tasks.ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendDataAsync(System.Collections.Generic.IEnumerable<SensorValueBase> items, System.Threading.CancellationToken token)
            {
                lock (_values)
                    _values.AddRange(items);

                _valueReceived.TrySetResult(true);
                return default;
            }

            public System.Threading.Tasks.ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendPriorityDataAsync(System.Collections.Generic.IEnumerable<SensorValueBase> items, System.Threading.CancellationToken token) =>
                SendDataAsync(items, token);

            public System.Threading.Tasks.ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendCommandAsync(System.Collections.Generic.IEnumerable<HSMSensorDataObjects.CommandRequestBase> commands, System.Threading.CancellationToken token) => default;

            public System.Threading.Tasks.ValueTask<HSMDataCollector.SyncQueue.Data.PackageSendingInfo> SendFileAsync(FileSensorValue file, System.Threading.CancellationToken token) => default;
        }
    }
}
