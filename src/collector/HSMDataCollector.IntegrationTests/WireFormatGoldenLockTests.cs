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
        public void Double_bool_and_doublebar_dtos_match_the_native_golden_bytes()
        {
            // The double/bool DTOs and the double-bar layout are the most drift-prone (runtime
            // shortest-double); lock them from the real serializer, not only the native unit tests.
            Assert.Equal(
                "{\"Type\":2,\"Value\":0.1,\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/d\"}",
                Wire(new DoubleSensorValue { Path = "p/d", Value = 0.1, Time = Epoch, Comment = null }));

            Assert.Equal(
                "{\"Type\":0,\"Value\":true,\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/b\"}",
                Wire(new BoolSensorValue { Path = "p/b", Value = true, Time = Epoch, Comment = null }));

            Assert.Equal(
                "{\"Type\":5,\"Min\":1.5,\"Max\":5.5,\"Mean\":3.25,\"FirstValue\":1.5,\"LastValue\":5.5,\"Percentiles\":null,"
                + "\"OpenTime\":\"1970-01-01T00:00:00Z\",\"CloseTime\":\"1970-01-01T00:00:02Z\",\"Count\":4,"
                + "\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/db\"}",
                Wire(new DoubleBarSensorValue { Path = "p/db", Min = 1.5, Max = 5.5, Mean = 3.25, FirstValue = 1.5, LastValue = 5.5, Count = 4, OpenTime = Epoch, CloseTime = Epoch.AddSeconds(2), Time = Epoch, Comment = null }));
        }

        [Fact]
        public void String_escaping_matches_the_native_golden_bytes()
        {
            // System.Text.Json's DEFAULT encoder escapes < > & ' + " and all non-ASCII as \uXXXX
            // (uppercase), backslash as \\, tab as \t — the double quote is ", NOT \". An
            // all-ASCII corpus never exercises this; these adversarial chars lock native EscapeJson
            // to the real encoder. Comment and string Value both go through the same encoder.
            const string tricky = "a<b>c&d'e+f\"g\\hé☃\tj";
            const string escaped = "a\\u003Cb\\u003Ec\\u0026d\\u0027e\\u002Bf\\u0022g\\\\h\\u00E9\\u2603\\tj";

            // Tricky in Comment, plain Value — this exact shape is mirrored by the native unit test
            // (native EscapeJson runs on Comment/Path; the string Value is escaped by the same
            // function at the call site).
            Assert.Equal(
                "{\"Type\":3,\"Value\":\"hi\",\"Comment\":\"" + escaped + "\","
                + "\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/esc\"}",
                Wire(new StringSensorValue { Path = "p/esc", Value = "hi", Time = Epoch, Comment = tricky }));

            // Tricky in the string Value position too, to lock value-side escaping.
            Assert.Equal(
                "{\"Type\":3,\"Value\":\"" + escaped + "\",\"Comment\":null,"
                + "\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/esc\"}",
                Wire(new StringSensorValue { Path = "p/esc", Value = tricky, Time = Epoch, Comment = null }));
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

        // Registration carrying a data alert (Alerts) and a TTL alert (TtlAlerts + TTLs). The same
        // bytes are pinned by the native NativeWireRegistrationWithAlertsMatchesNetByteLayout unit
        // test, which builds this exact alert through the C ABI. Locks: AlertUpdateRequest field
        // order, numeric enums, the emoji icon escaped to ⚠, ConfirmationPeriod ticks, and the
        // TTL-alert -> TTLs coupling.
        [Fact]
        public void Registration_with_alerts_matches_the_native_golden_bytes()
        {
            var registration = new AddOrUpdateSensorRequest
            {
                Path = "p/alert",
                SensorType = SensorType.IntSensor,
                Description = "d",
                OriginalUnit = Unit.MB,
                Alerts = new List<AlertUpdateRequest>
                {
                    new AlertUpdateRequest
                    {
                        Conditions = new List<AlertConditionUpdate>
                        {
                            new AlertConditionUpdate { Combination = AlertCombination.And, Operation = AlertOperation.GreaterThan, Property = AlertProperty.Value, Target = new TargetValue { Type = TargetType.Const, Value = "42" } },
                            new AlertConditionUpdate { Combination = AlertCombination.Or, Operation = AlertOperation.IsOk, Property = AlertProperty.Status, Target = new TargetValue { Type = TargetType.LastValue, Value = null } },
                        },
                        Status = SensorStatus.Error,
                        DestinationMode = AlertDestinationMode.AllChats,
                        Template = "spike",
                        Icon = "⚠",
                        IsDisabled = false,
                        ConfirmationPeriod = 3000000000,
                    },
                },
                TtlAlerts = new List<AlertUpdateRequest>
                {
                    new AlertUpdateRequest
                    {
                        Conditions = new List<AlertConditionUpdate>(),
                        Status = SensorStatus.Ok,
                        DestinationMode = AlertDestinationMode.FromParent,
                        Template = "inactive",
                        Icon = null,
                    },
                },
                TTLs = new List<long?> { 600000000 },
            };

            Assert.Equal(
                "{\"Type\":0,\"Alerts\":[{\"Conditions\":[{\"Combination\":0,\"Operation\":2,\"Property\":20,\"Target\":{\"Type\":0,\"Value\":\"42\"}},"
                + "{\"Combination\":1,\"Operation\":22,\"Property\":0,\"Target\":{\"Type\":1,\"Value\":null}}],"
                + "\"Status\":3,\"DestinationMode\":200,\"Template\":\"spike\",\"Icon\":\"\\u26A0\",\"IsDisabled\":false,"
                + "\"ConfirmationPeriod\":3000000000,\"ScheduledNotificationTime\":null,\"ScheduledRepeatMode\":null,\"ScheduledInstantSend\":null}],"
                + "\"TtlAlerts\":[{\"Conditions\":[],\"Status\":1,\"DestinationMode\":3,\"Template\":\"inactive\",\"Icon\":null,\"IsDisabled\":false,"
                + "\"ConfirmationPeriod\":null,\"ScheduledNotificationTime\":null,\"ScheduledRepeatMode\":null,\"ScheduledInstantSend\":null}],"
                + "\"TtlAlert\":null,\"SensorType\":1,\"Description\":\"d\",\"DefaultChats\":null,\"KeepHistory\":null,\"SelfDestroy\":null,"
                + "\"TTLs\":[600000000],\"TTL\":null,\"Statistics\":null,\"IsSingletonSensor\":null,\"AggregateData\":null,\"EnableGrafana\":null,"
                + "\"OriginalUnit\":3,\"DisplayUnit\":null,\"DefaultAlertsOptions\":0,\"IsForceUpdate\":false,\"EnumOptions\":null,"
                + "\"Key\":null,\"Path\":\"p/alert\"}",
                WireCommand(registration));
        }

        // Full SensorOptions registration surface (#1098 §6): KeepHistory/SelfDestroy ticks,
        // Statistics(EMA), DisplayUnit, IsSingletonSensor/AggregateData/EnableGrafana. Same bytes as
        // native NativeWireRegistrationFullOptionsMatchesNetByteLayout.
        [Fact]
        public void Registration_full_options_match_the_native_golden_bytes()
        {
            var registration = new AddOrUpdateSensorRequest
            {
                Path = "comp/mod/full/opts",
                SensorType = SensorType.IntSensor,
                Description = "d",
                OriginalUnit = Unit.MB,
                TTLs = new List<long?> { 600000000 },
                KeepHistory = 6000000000,
                SelfDestroy = 12000000000,
                DisplayUnit = 3,
                Statistics = StatisticsOptions.EMA,
                IsSingletonSensor = true,
                AggregateData = true,
                EnableGrafana = true,
            };

            Assert.Equal(
                "{\"Type\":0,\"Alerts\":null,\"TtlAlerts\":null,\"TtlAlert\":null,\"SensorType\":1,\"Description\":\"d\","
                + "\"DefaultChats\":null,\"KeepHistory\":6000000000,\"SelfDestroy\":12000000000,\"TTLs\":[600000000],\"TTL\":null,"
                + "\"Statistics\":1,\"IsSingletonSensor\":true,\"AggregateData\":true,\"EnableGrafana\":true,"
                + "\"OriginalUnit\":3,\"DisplayUnit\":3,\"DefaultAlertsOptions\":0,\"IsForceUpdate\":false,\"EnumOptions\":null,"
                + "\"Key\":null,\"Path\":\"comp/mod/full/opts\"}",
                WireCommand(registration));
        }
    }
}
