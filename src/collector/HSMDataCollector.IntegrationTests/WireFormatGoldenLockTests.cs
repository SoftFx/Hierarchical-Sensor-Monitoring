using System;
using System.Collections.Generic;

using HSMDataCollector.Client.HttpsClient;

using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;

using Xunit;

namespace HSMDataCollector.IntegrationTests
{
    // Cross-language wire golden lock (#1096 §15). These assert the EXACT bytes the real
    // System.Text.Json path (HttpRequest&lt;T&gt;) emits on net8 — the same byte strings the native
    // C++ collector pins in its `native_wire_*` unit tests (src/native/collector). If the .NET
    // serializer ever drifts (property order, ISO/TimeSpan format, List&lt;byte&gt; array, emitted
    // nulls), THIS test fails first, signalling that the native expected bytes must be updated in
    // lockstep. net8 is the canonical Core/shortest-double runtime the conformance corpus uses;
    // net472 doubles diverge and are out of the golden scope (documented in number_format_contract).
    public class WireFormatGoldenLockTests
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static string Wire(SensorValueBase v) => new HttpRequest<SensorValueBase>(v, "").Content;

        private static string WireCommand(CommandRequestBase c) => new HttpRequest<CommandRequestBase>(c, "").Content;

        [Fact]
        public void Value_dtos_match_the_native_golden_bytes()
        {
            Assert.Equal(
                "{\"Type\":1,\"Value\":42,\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/int\"}",
                Wire(new IntSensorValue { Path = "p/int", Value = 42, Time = Epoch, Comment = null }));

            Assert.Equal(
                "{\"Type\":3,\"Value\":\"hi\",\"Comment\":\"note\",\"Time\":\"1970-01-01T00:00:01.5Z\",\"Status\":2,\"Key\":null,\"Path\":\"p/s\"}",
                Wire(new StringSensorValue { Path = "p/s", Value = "hi", Time = Epoch.AddMilliseconds(1500), Comment = "note", Status = SensorStatus.Warning }));

            Assert.Equal(
                "{\"Type\":7,\"Value\":\"1.02:03:04.0050000\",\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/ts\"}",
                Wire(new TimeSpanSensorValue { Path = "p/ts", Value = new TimeSpan(1, 2, 3, 4, 5), Time = Epoch, Comment = null }));

            Assert.Equal(
                "{\"Type\":8,\"Value\":\"1.2.3.4\",\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/v\"}",
                Wire(new VersionSensorValue { Path = "p/v", Value = new Version(1, 2, 3, 4), Time = Epoch, Comment = null }));
        }

        [Fact]
        public void Bar_and_file_dtos_match_the_native_golden_bytes()
        {
            Assert.Equal(
                "{\"Type\":4,\"Min\":1,\"Max\":5,\"Mean\":3,\"FirstValue\":1,\"LastValue\":5,\"Percentiles\":null,"
                + "\"OpenTime\":\"1970-01-01T00:00:00Z\",\"CloseTime\":\"1970-01-01T00:00:02Z\",\"Count\":5,"
                + "\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/ib\"}",
                Wire(new IntBarSensorValue { Path = "p/ib", Min = 1, Max = 5, Mean = 3, FirstValue = 1, LastValue = 5, Count = 5, OpenTime = Epoch, CloseTime = Epoch.AddSeconds(2), Time = Epoch, Comment = null }));

            Assert.Equal(
                "{\"Type\":6,\"Extension\":\"txt\",\"Name\":\"n\",\"Value\":[104,105],\"Comment\":null,"
                + "\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/f\"}",
                Wire(new FileSensorValue { Path = "p/f", Value = new List<byte> { 104, 105 }, Name = "n", Extension = "txt", Time = Epoch, Comment = null }));
        }

        [Fact]
        public void Registration_matches_the_native_golden_bytes()
        {
            var registration = new AddOrUpdateSensorRequest
            {
                Path = "p/int",
                SensorType = SensorType.IntSensor,
                Description = "d",
                TTLs = new List<long?> { 600000000 },
                OriginalUnit = Unit.MB,
                EnumOptions = new List<EnumOption> { new EnumOption { Key = 1, Value = "v", Description = "ed", Color = -16711936 } },
            };

            Assert.Equal(
                "{\"Type\":0,\"Alerts\":null,\"TtlAlerts\":null,\"TtlAlert\":null,\"SensorType\":1,\"Description\":\"d\","
                + "\"DefaultChats\":null,\"KeepHistory\":null,\"SelfDestroy\":null,\"TTLs\":[600000000],\"TTL\":null,"
                + "\"Statistics\":null,\"IsSingletonSensor\":null,\"AggregateData\":null,\"EnableGrafana\":null,"
                + "\"OriginalUnit\":3,\"DisplayUnit\":null,\"DefaultAlertsOptions\":0,\"IsForceUpdate\":false,"
                + "\"EnumOptions\":[{\"Key\":1,\"Value\":\"v\",\"Description\":\"ed\",\"Color\":-16711936}],"
                + "\"Key\":null,\"Path\":\"p/int\"}",
                WireCommand(registration));
        }
    }
}
