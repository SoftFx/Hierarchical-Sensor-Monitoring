using System;
using System.Net;
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

        private static CollectorOptions CreateOptions() => new CollectorOptions
        {
            AccessKey = "normalization-test-key",
            ClientName = "normalization-test-client",
            ComputerName = "normalization-test-host",
            Module = "normalization-test-module",
            ServerAddress = "https://127.0.0.1",
        };
    }
}
