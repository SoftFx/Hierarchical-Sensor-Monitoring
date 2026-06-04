using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    public sealed class CollectorConformanceTests
    {
        [Theory]
        [MemberData(nameof(CollectorContractCases))]
        public async Task Collector_contract_matches_shared_fixture(string caseName, IReadOnlyList<ContractStep> steps)
        {
            Assert.False(string.IsNullOrWhiteSpace(caseName));

            var state = new ContractState();

            try
            {
                foreach (var step in steps)
                    await ExecuteStepAsync(state, step).ConfigureAwait(false);
            }
            finally
            {
                if (state.Collector != null)
                    await state.Collector.Stop().ConfigureAwait(false);

                state.Collector?.Dispose();
            }
        }

        public static IEnumerable<object[]> CollectorContractCases()
        {
            foreach (var contractFile in FindContractFiles())
            {
                var contractName = Path.GetFileNameWithoutExtension(contractFile);

                foreach (var testCase in ReadContractCases(contractFile))
                    yield return new object[] { contractName + ":" + testCase.Key, testCase.Value };
            }
        }

        private static async Task ExecuteStepAsync(ContractState state, ContractStep step)
        {
            switch (step.Action)
            {
                case "create_collector":
                    state.Sender = new RecordingSender();
                    state.Collector = CreateCollector(state.Sender);
                    break;

                case "create_collector_with_identity":
                    state.Sender = new RecordingSender();
                    state.Collector = CreateCollector(
                        state.Sender,
                        computerName: ExpandTextToken(step.Arg(0)),
                        module: ExpandTextToken(step.Arg(1)));
                    break;

                case "expect_create_collector_rejected":
                    ExpectCollectorCreateRejected(
                        accessKey: ExpandTextToken(step.Arg(0)),
                        serverAddress: ExpandTextToken(step.Arg(1)),
                        port: step.TryArg(2, out var port) ? int.Parse(port) : CollectorOptions.DefaultPort);
                    break;

                case "start":
                    await state.Collector.Start().ConfigureAwait(false);
                    break;

                case "stop":
                    await state.Collector.Stop().ConfigureAwait(false);
                    break;

                case "create_int_sensor":
                    AddSensor(state, state.IntSensors, state.Collector.CreateIntSensor(ExpandTextToken(step.Arg(0))));
                    break;

                case "create_bool_sensor":
                    AddSensor(state, state.BoolSensors, state.Collector.CreateBoolSensor(ExpandTextToken(step.Arg(0))));
                    break;

                case "create_double_sensor":
                    AddSensor(state, state.DoubleSensors, state.Collector.CreateDoubleSensor(ExpandTextToken(step.Arg(0))));
                    break;

                case "create_string_sensor":
                    AddSensor(state, state.StringSensors, state.Collector.CreateStringSensor(ExpandTextToken(step.Arg(0))));
                    break;

                case "create_enum_sensor":
                    AddSensor(state, state.EnumSensors, state.Collector.CreateEnumSensor(ExpandTextToken(step.Arg(0))));
                    break;

                case "create_last_int_sensor":
                    AddSensor(state, state.IntSensors, state.Collector.CreateLastValueIntSensor(step.Arg(0), int.Parse(step.Arg(1))));
                    break;

                case "create_last_bool_sensor":
                    AddSensor(state, state.BoolSensors, state.Collector.CreateLastValueBoolSensor(step.Arg(0), bool.Parse(step.Arg(1))));
                    break;

                case "create_last_double_sensor":
                    AddSensor(state, state.DoubleSensors, state.Collector.CreateLastValueDoubleSensor(
                        step.Arg(0),
                        double.Parse(step.Arg(1), CultureInfo.InvariantCulture)));
                    break;

                case "create_last_string_sensor":
                    AddSensor(state, state.StringSensors, state.Collector.CreateLastValueStringSensor(step.Arg(0), ExpandTextToken(step.Arg(1))));
                    break;

                case "create_int_sensors":
                    CreateIntSensors(state, int.Parse(step.Arg(0)), step.Arg(1));
                    break;

                case "create_mixed_instant_sensors":
                    CreateMixedInstantSensors(state, int.Parse(step.Arg(0)), step.Arg(1));
                    break;

                case "expect_create_int_sensor_rejected":
                    ExpectCreateRejected(() => state.Collector.CreateIntSensor(step.Arg(0)));
                    break;

                case "expect_create_last_int_sensor_rejected":
                    ExpectCreateRejected(() => state.Collector.CreateLastValueIntSensor(step.Arg(0), int.Parse(step.Arg(1))));
                    break;

                case "expect_create_last_bool_sensor_rejected":
                    ExpectCreateRejected(() => state.Collector.CreateLastValueBoolSensor(step.Arg(0), bool.Parse(step.Arg(1))));
                    break;

                case "expect_create_last_double_sensor_rejected":
                    ExpectCreateRejected(() => state.Collector.CreateLastValueDoubleSensor(
                        step.Arg(0),
                        ParseDouble(step.Arg(1))));
                    break;

                case "expect_create_last_string_sensor_rejected":
                    ExpectCreateRejected(() => state.Collector.CreateLastValueStringSensor(step.Arg(0), ExpandTextToken(step.Arg(1))));
                    break;

                case "add_int":
                    state.IntSensors[int.Parse(step.Arg(0))]
                        .AddValue(int.Parse(step.Arg(1)), ParseStatus(step.Arg(2)), ExpandTextToken(step.Arg(3)));
                    break;

                case "add_bool":
                    state.BoolSensors[int.Parse(step.Arg(0))]
                        .AddValue(bool.Parse(step.Arg(1)), ParseStatus(step.Arg(2)), ExpandTextToken(step.Arg(3)));
                    break;

                case "add_double":
                    state.DoubleSensors[int.Parse(step.Arg(0))]
                        .AddValue(ParseDouble(step.Arg(1)), ParseStatus(step.Arg(2)), ExpandTextToken(step.Arg(3)));
                    break;

                case "add_string":
                    state.StringSensors[int.Parse(step.Arg(0))]
                        .AddValue(ExpandTextToken(step.Arg(1)), ParseStatus(step.Arg(2)), ExpandTextToken(step.Arg(3)));
                    break;

                case "add_enum":
                    state.EnumSensors[int.Parse(step.Arg(0))]
                        .AddValue(int.Parse(step.Arg(1)), ParseStatus(step.Arg(2)), ExpandTextToken(step.Arg(3)));
                    break;

                case "expect_add_int_rejected":
                    ExpectAddRejected(
                        state,
                        () => state.IntSensors[int.Parse(step.Arg(0))]
                            .AddValue(int.Parse(step.Arg(1)), ParseRawStatus(step.Arg(2)), ExpandTextToken(step.Arg(3))));
                    break;

                case "expect_add_bool_rejected":
                    ExpectAddRejected(
                        state,
                        () => state.BoolSensors[int.Parse(step.Arg(0))]
                            .AddValue(bool.Parse(step.Arg(1)), ParseRawStatus(step.Arg(2)), ExpandTextToken(step.Arg(3))));
                    break;

                case "expect_add_double_rejected":
                    ExpectAddRejected(
                        state,
                        () => state.DoubleSensors[int.Parse(step.Arg(0))]
                            .AddValue(ParseDouble(step.Arg(1)), ParseRawStatus(step.Arg(2)), ExpandTextToken(step.Arg(3))));
                    break;

                case "expect_add_string_rejected":
                    ExpectAddRejected(
                        state,
                        () => state.StringSensors[int.Parse(step.Arg(0))]
                            .AddValue(ExpandTextToken(step.Arg(1)), ParseRawStatus(step.Arg(2)), ExpandTextToken(step.Arg(3))));
                    break;

                case "expect_add_enum_rejected":
                    ExpectAddRejected(
                        state,
                        () => state.EnumSensors[int.Parse(step.Arg(0))]
                            .AddValue(int.Parse(step.Arg(1)), ParseRawStatus(step.Arg(2)), ExpandTextToken(step.Arg(3))));
                    break;

                case "dispose_sensor":
                    DisposeSensor(state, int.Parse(step.Arg(0)));
                    break;

                case "add_int_sequence":
                    AddIntSequence(
                        state,
                        sensorCount: int.Parse(step.Arg(0)),
                        valuesPerSensor: int.Parse(step.Arg(1)),
                        startValue: int.Parse(step.Arg(2)),
                        status: ParseStatus(step.Arg(3)),
                        comment: ExpandTextToken(step.Arg(4)));
                    break;

                case "add_int_parallel":
                    await AddIntParallelAsync(
                        state,
                        workerCount: int.Parse(step.Arg(0)),
                        valuesPerWorker: int.Parse(step.Arg(1)),
                        sensorCount: int.Parse(step.Arg(2)),
                        status: ParseStatus(step.Arg(3)),
                        comment: ExpandTextToken(step.Arg(4))).ConfigureAwait(false);
                    break;

                case "add_mixed_instant_sequence":
                    AddMixedInstantSequence(
                        state,
                        setCount: int.Parse(step.Arg(0)),
                        valuesPerSet: int.Parse(step.Arg(1)),
                        startValue: int.Parse(step.Arg(2)),
                        status: ParseStatus(step.Arg(3)),
                        comment: ExpandTextToken(step.Arg(4)));
                    break;

                case "add_mixed_instant_parallel":
                    await AddMixedInstantParallelAsync(
                        state,
                        workerCount: int.Parse(step.Arg(0)),
                        valuesPerWorker: int.Parse(step.Arg(1)),
                        setCount: int.Parse(step.Arg(2)),
                        status: ParseStatus(step.Arg(3)),
                        comment: ExpandTextToken(step.Arg(4))).ConfigureAwait(false);
                    break;

                case "expect_conflicting_mixed_creates_rejected_parallel":
                    await ExpectConflictingMixedCreatesRejectedParallelAsync(
                        state,
                        workerCount: int.Parse(step.Arg(0)),
                        pathCount: int.Parse(step.Arg(1)),
                        pathPrefix: step.Arg(2)).ConfigureAwait(false);
                    break;

                case "repeat_start_stop_add":
                    await RepeatStartStopAddAsync(
                        state,
                        cycles: int.Parse(step.Arg(0)),
                        sensorIndex: int.Parse(step.Arg(1)),
                        status: ParseStatus(step.Arg(2)),
                        commentPrefix: ExpandTextToken(step.Arg(3))).ConfigureAwait(false);
                    break;

                case "expect_sent_count":
                    var timeout = step.TryArg(1, out var seconds)
                        ? TimeSpan.FromSeconds(int.Parse(seconds))
                        : TimeSpan.FromSeconds(2);

                    Assert.True(
                        await state.Sender.WaitForCountAsync(int.Parse(step.Arg(0)), timeout).ConfigureAwait(false),
                        $"Expected {step.Arg(0)} sent value(s), got {state.Sender.Values.Count}.");
                    break;

                case "expect_payload_contains":
                    Assert.Contains(step.Arg(1), PayloadText(state.Sender.Values[int.Parse(step.Arg(0))]));
                    break;

                case "expect_payload_not_contains":
                    Assert.DoesNotContain(step.Arg(1), PayloadText(state.Sender.Values[int.Parse(step.Arg(0))]));
                    break;

                case "expect_comment_length":
                    Assert.Equal(int.Parse(step.Arg(1)), state.Sender.Values[int.Parse(step.Arg(0))].Comment?.Length ?? 0);
                    break;

                case "expect_all_payloads_contain":
                    Assert.All(state.Sender.Values, value => Assert.Contains(step.Arg(0), PayloadText(value)));
                    break;

                case "expect_payload_value_sequence":
                    ExpectPayloadValueSequence(
                        state,
                        startPayloadIndex: int.Parse(step.Arg(0)),
                        count: int.Parse(step.Arg(1)),
                        startValue: int.Parse(step.Arg(2)));
                    break;

                case "expect_payload_type_counts":
                    ExpectPayloadTypeCounts(
                        state,
                        boolCount: int.Parse(step.Arg(0)),
                        intCount: int.Parse(step.Arg(1)),
                        doubleCount: int.Parse(step.Arg(2)),
                        stringCount: int.Parse(step.Arg(3)),
                        enumCount: int.Parse(step.Arg(4)));
                    break;

                default:
                    throw new InvalidOperationException($"Unknown conformance action '{step.Action}'.");
            }
        }

        private static DataCollector CreateCollector(
            IDataSender sender,
            string computerName = "conformance-host",
            string module = "conformance-module",
            string accessKey = "conformance-key",
            string serverAddress = "https://localhost",
            int port = 443)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = accessKey,
                ServerAddress = serverAddress,
                Port = port,
                ClientName = "conformance-client",
                ComputerName = computerName,
                Module = module,
                DataSender = sender,
                MaxQueueSize = 20000,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(20),
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMilliseconds(100),
                MaxDeduplicatedMessages = 100,
            });
        }

        private static void CreateIntSensors(ContractState state, int count, string pathPrefix)
        {
            for (var i = 0; i < count; i++)
                AddSensor(state, state.IntSensors, state.Collector.CreateIntSensor(pathPrefix + "/" + i));
        }

        private static void CreateMixedInstantSensors(ContractState state, int count, string pathPrefix)
        {
            for (var i = 0; i < count; i++)
            {
                AddSensor(state, state.BoolSensors, state.Collector.CreateBoolSensor(pathPrefix + "/" + i + "/bool"));
                AddSensor(state, state.IntSensors, state.Collector.CreateIntSensor(pathPrefix + "/" + i + "/int"));
                AddSensor(state, state.DoubleSensors, state.Collector.CreateDoubleSensor(pathPrefix + "/" + i + "/double"));
                AddSensor(state, state.StringSensors, state.Collector.CreateStringSensor(pathPrefix + "/" + i + "/string"));
                AddSensor(state, state.EnumSensors, state.Collector.CreateEnumSensor(pathPrefix + "/" + i + "/enum"));
            }
        }

        private static void AddSensor<T>(ContractState state, List<T> typedSensors, T sensor)
        {
            typedSensors.Add(sensor);
            state.Sensors.Add((IDisposable)sensor);
        }

        private static void AddIntSequence(
            ContractState state,
            int sensorCount,
            int valuesPerSensor,
            int startValue,
            SensorStatus status,
            string comment)
        {
            for (var sensorIndex = 0; sensorIndex < sensorCount; sensorIndex++)
            {
                for (var valueIndex = 0; valueIndex < valuesPerSensor; valueIndex++)
                {
                    var value = startValue + sensorIndex * valuesPerSensor + valueIndex;
                    state.IntSensors[sensorIndex].AddValue(value, status, comment);
                }
            }
        }

        private static Task AddIntParallelAsync(
            ContractState state,
            int workerCount,
            int valuesPerWorker,
            int sensorCount,
            SensorStatus status,
            string comment)
        {
            var tasks = Enumerable.Range(0, workerCount)
                .Select(worker => Task.Run(() =>
                {
                    for (var valueIndex = 0; valueIndex < valuesPerWorker; valueIndex++)
                    {
                        var sensorIndex = (worker + valueIndex) % sensorCount;
                        var value = worker * valuesPerWorker + valueIndex;
                        state.IntSensors[sensorIndex].AddValue(value, status, comment);
                    }
                }))
                .ToArray();

            return Task.WhenAll(tasks);
        }

        private static void AddMixedInstantSequence(
            ContractState state,
            int setCount,
            int valuesPerSet,
            int startValue,
            SensorStatus status,
            string comment)
        {
            for (var setIndex = 0; setIndex < setCount; setIndex++)
            {
                for (var valueIndex = 0; valueIndex < valuesPerSet; valueIndex++)
                {
                    var value = startValue + setIndex * valuesPerSet + valueIndex;
                    AddMixedInstantValue(state, setIndex, value, status, comment);
                }
            }
        }

        private static Task AddMixedInstantParallelAsync(
            ContractState state,
            int workerCount,
            int valuesPerWorker,
            int setCount,
            SensorStatus status,
            string comment)
        {
            var tasks = Enumerable.Range(0, workerCount)
                .Select(worker => Task.Run(() =>
                {
                    for (var valueIndex = 0; valueIndex < valuesPerWorker; valueIndex++)
                    {
                        var setIndex = (worker + valueIndex) % setCount;
                        var value = worker * valuesPerWorker + valueIndex;
                        AddMixedInstantValue(state, setIndex, value, status, comment);
                    }
                }))
                .ToArray();

            return Task.WhenAll(tasks);
        }

        private static Task ExpectConflictingMixedCreatesRejectedParallelAsync(
            ContractState state,
            int workerCount,
            int pathCount,
            string pathPrefix)
        {
            var tasks = Enumerable.Range(0, workerCount)
                .Select(worker => Task.Run(() =>
                {
                    for (var pathIndex = worker; pathIndex < pathCount; pathIndex += workerCount)
                    {
                        var path = pathPrefix + "/" + pathIndex.ToString(CultureInfo.InvariantCulture);

                        ExpectCreateRejected(() => state.Collector.CreateBoolSensor(path));
                        ExpectCreateRejected(() => state.Collector.CreateDoubleSensor(path));
                        ExpectCreateRejected(() => state.Collector.CreateStringSensor(path));
                        ExpectCreateRejected(() => state.Collector.CreateEnumSensor(path));
                    }
                }))
                .ToArray();

            return Task.WhenAll(tasks);
        }

        private static void ExpectCreateRejected<TSensor>(Func<TSensor> createSensor)
        {
            Assert.NotNull(Record.Exception(() => createSensor()));
        }

        private static void ExpectCollectorCreateRejected(string accessKey, string serverAddress, int port)
        {
            using (var sender = new RecordingSender())
            {
                DataCollector collector = null;
                var exception = Record.Exception(() =>
                {
                    collector = CreateCollector(sender, accessKey: accessKey, serverAddress: serverAddress, port: port);
                });

                collector?.Dispose();

                Assert.NotNull(exception);
            }
        }

        private static void ExpectAddRejected(ContractState state, Action addValue)
        {
            var before = state.Sender.Values.Count;
            var exception = Record.Exception(addValue);

            Assert.Null(exception);
            Assert.Equal(before, state.Sender.Values.Count);
        }

        private static void DisposeSensor(ContractState state, int sensorIndex)
        {
            state.Sensors[sensorIndex]?.Dispose();
            state.Sensors[sensorIndex] = null;
        }

        private static void AddMixedInstantValue(ContractState state, int setIndex, int value, SensorStatus status, string comment)
        {
            state.BoolSensors[setIndex].AddValue(value % 2 == 0, status, comment);
            state.IntSensors[setIndex].AddValue(value, status, comment);
            state.DoubleSensors[setIndex].AddValue(value + 0.25, status, comment);
            state.StringSensors[setIndex].AddValue("value-" + value.ToString(CultureInfo.InvariantCulture), status, comment);
            state.EnumSensors[setIndex].AddValue(value % 4, status, comment);
        }

        private static async Task RepeatStartStopAddAsync(
            ContractState state,
            int cycles,
            int sensorIndex,
            SensorStatus status,
            string commentPrefix)
        {
            for (var cycle = 0; cycle < cycles; cycle++)
            {
                await state.Collector.Start().ConfigureAwait(false);
                state.IntSensors[sensorIndex].AddValue(cycle, status, commentPrefix + "-" + cycle);
                await state.Collector.Stop().ConfigureAwait(false);
            }
        }

        private static void ExpectPayloadValueSequence(ContractState state, int startPayloadIndex, int count, int startValue)
        {
            for (var offset = 0; offset < count; offset++)
            {
                var payload = PayloadText(state.Sender.Values[startPayloadIndex + offset]);
                Assert.Contains("\"Value\":" + (startValue + offset), payload);
            }
        }

        private static void ExpectPayloadTypeCounts(
            ContractState state,
            int boolCount,
            int intCount,
            int doubleCount,
            int stringCount,
            int enumCount)
        {
            var payloads = state.Sender.Values.Select(PayloadText).ToArray();

            Assert.Equal(boolCount, payloads.Count(payload => payload.Contains("\"Type\":0,")));
            Assert.Equal(intCount, payloads.Count(payload => payload.Contains("\"Type\":1,")));
            Assert.Equal(doubleCount, payloads.Count(payload => payload.Contains("\"Type\":2,")));
            Assert.Equal(stringCount, payloads.Count(payload => payload.Contains("\"Type\":3,")));
            Assert.Equal(enumCount, payloads.Count(payload => payload.Contains("\"Type\":10,")));
        }

        private static string PayloadText(SensorValueBase value)
        {
            return "{" +
                   $"\"Type\":{(int)value.Type}," +
                   $"\"Path\":\"{EscapeJson(value.Path)}\"," +
                   $"\"Value\":{PayloadValueText(value)}," +
                   $"\"Status\":{(int)value.Status}," +
                   $"\"Comment\":\"{EscapeJson(value.Comment)}\"" +
                   "}";
        }

        private static string PayloadValueText(SensorValueBase value)
        {
            if (value is BoolSensorValue boolValue)
                return boolValue.Value ? "true" : "false";

            if (value is IntSensorValue intValue)
                return intValue.Value.ToString(CultureInfo.InvariantCulture);

            if (value is EnumSensorValue enumValue)
                return enumValue.Value.ToString(CultureInfo.InvariantCulture);

            if (value is DoubleSensorValue doubleValue)
                return doubleValue.Value.ToString("R", CultureInfo.InvariantCulture);

            if (value is StringSensorValue stringValue)
                return "\"" + EscapeJson(stringValue.Value) + "\"";

            throw new InvalidOperationException($"Unsupported conformance payload type '{value.GetType().FullName}'.");
        }

        private static string EscapeJson(string value)
        {
            if (value == null)
                return null;

            var builder = new StringBuilder(value.Length);

            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (ch < 0x20)
                            builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(ch);
                        break;
                }
            }

            return builder.ToString();
        }

        private static double ParseDouble(string value)
        {
            if (string.Equals(value, "NaN", StringComparison.Ordinal))
                return double.NaN;
            if (string.Equals(value, "Infinity", StringComparison.Ordinal))
                return double.PositiveInfinity;
            if (string.Equals(value, "-Infinity", StringComparison.Ordinal))
                return double.NegativeInfinity;

            return double.Parse(value, CultureInfo.InvariantCulture);
        }

        private static SensorStatus ParseStatus(string value)
        {
            return (SensorStatus)Enum.Parse(typeof(SensorStatus), value);
        }

        private static SensorStatus ParseRawStatus(string value) => (SensorStatus)int.Parse(value, CultureInfo.InvariantCulture);

        private static string ExpandTextToken(string value)
        {
            const string repeatPrefix = "repeat:";

            if (!value.StartsWith(repeatPrefix, StringComparison.Ordinal))
            {
                if (string.Equals(value, "token:json-special", StringComparison.Ordinal))
                    return "quote\"slash\\tab\tnewline\n";
                if (string.Equals(value, "token:control-01", StringComparison.Ordinal))
                    return "a" + '\u0001' + "b";
                if (string.Equals(value, "token:control-02", StringComparison.Ordinal))
                    return "path" + '\u0002' + "part";
                if (string.Equals(value, "token:control-1f", StringComparison.Ordinal))
                    return "bad" + '\u001f' + "comment";
                if (string.Equals(value, "token:blank", StringComparison.Ordinal))
                    return " \t ";
                if (string.Equals(value, "token:null", StringComparison.Ordinal))
                    return null;

                return value;
            }

            var separator = value.IndexOf(':', repeatPrefix.Length);
            if (separator <= repeatPrefix.Length)
                throw new InvalidOperationException($"Invalid repeat token '{value}'.");

            var ch = value[repeatPrefix.Length];
            var count = int.Parse(value.Substring(separator + 1));

            return new string(ch, count);
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<ContractStep>> ReadContractCases(string path)
        {
            var cases = new Dictionary<string, List<ContractStep>>(StringComparer.Ordinal);

            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                var parts = line.Split('|');
                if (parts.Length < 2)
                    throw new InvalidOperationException($"Invalid conformance line: {line}");

                List<ContractStep> steps;
                if (!cases.TryGetValue(parts[0], out steps))
                {
                    steps = new List<ContractStep>();
                    cases.Add(parts[0], steps);
                }

                steps.Add(new ContractStep(parts[1], parts.Skip(2).ToArray()));
            }

            return cases.ToDictionary(x => x.Key, x => (IReadOnlyList<ContractStep>)x.Value, StringComparer.Ordinal);
        }

        private static IReadOnlyList<string> FindContractFiles()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "tests", "conformance", "collector");
                if (Directory.Exists(candidate))
                    return Directory.GetFiles(candidate, "*.hsmtest").OrderBy(x => x, StringComparer.Ordinal).ToArray();

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Cannot find collector conformance contracts.");
        }

        public sealed class ContractStep
        {
            private readonly string[] _args;

            public ContractStep(string action, string[] args)
            {
                Action = action;
                _args = args;
            }

            public string Action { get; }

            public string Arg(int index) => _args[index];

            public bool TryArg(int index, out string value)
            {
                if (index < _args.Length)
                {
                    value = _args[index];
                    return true;
                }

                value = null;
                return false;
            }

            public override string ToString() => Action;
        }

        private sealed class ContractState
        {
            public DataCollector Collector { get; set; }

            public RecordingSender Sender { get; set; }

            public List<IDisposable> Sensors { get; } = new List<IDisposable>();

            public List<IInstantValueSensor<int>> IntSensors { get; } = new List<IInstantValueSensor<int>>();

            public List<IInstantValueSensor<bool>> BoolSensors { get; } = new List<IInstantValueSensor<bool>>();

            public List<IInstantValueSensor<double>> DoubleSensors { get; } = new List<IInstantValueSensor<double>>();

            public List<IInstantValueSensor<string>> StringSensors { get; } = new List<IInstantValueSensor<string>>();

            public List<IInstantValueSensor<int>> EnumSensors { get; } = new List<IInstantValueSensor<int>>();
        }

        private sealed class RecordingSender : IDataSender
        {
            private readonly object _lock = new object();

            public IReadOnlyList<SensorValueBase> Values
            {
                get
                {
                    lock (_lock)
                        return _values.ToList();
                }
            }

            private readonly List<SensorValueBase> _values = new List<SensorValueBase>();

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                lock (_lock)
                    _values.AddRange(items);

                return default;
            }

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
                => SendDataAsync(items, token);

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => default;

            public async Task<bool> WaitForCountAsync(int count, TimeSpan timeout)
            {
                var stopAt = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < stopAt)
                {
                    if (Values.Count == count)
                        return true;

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return Values.Count == count;
            }
        }
    }
}
