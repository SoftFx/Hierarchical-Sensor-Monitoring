using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
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

                case "start":
                    await state.Collector.Start().ConfigureAwait(false);
                    break;

                case "stop":
                    await state.Collector.Stop().ConfigureAwait(false);
                    break;

                case "create_int_sensor":
                    state.IntSensors.Add(state.Collector.CreateIntSensor(step.Arg(0)));
                    break;

                case "create_int_sensors":
                    CreateIntSensors(state, int.Parse(step.Arg(0)), step.Arg(1));
                    break;

                case "add_int":
                    state.IntSensors[int.Parse(step.Arg(0))]
                        .AddValue(int.Parse(step.Arg(1)), ParseStatus(step.Arg(2)), ExpandTextToken(step.Arg(3)));
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

                default:
                    throw new InvalidOperationException($"Unknown conformance action '{step.Action}'.");
            }
        }

        private static DataCollector CreateCollector(IDataSender sender)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "conformance-key",
                ClientName = "conformance-client",
                ComputerName = "conformance-host",
                Module = "conformance-module",
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
                state.IntSensors.Add(state.Collector.CreateIntSensor(pathPrefix + "/" + i));
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

        private static string PayloadText(SensorValueBase value)
        {
            var intValue = value as IntSensorValue;

            return "{" +
                   $"\"Type\":{(int)value.Type}," +
                   $"\"Path\":\"{EscapeJson(value.Path)}\"," +
                   $"\"Value\":{intValue?.Value}," +
                   $"\"Status\":{(int)value.Status}," +
                   $"\"Comment\":\"{EscapeJson(value.Comment)}\"" +
                   "}";
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
                        builder.Append(ch);
                        break;
                }
            }

            return builder.ToString();
        }

        private static SensorStatus ParseStatus(string value)
        {
            return (SensorStatus)Enum.Parse(typeof(SensorStatus), value);
        }

        private static string ExpandTextToken(string value)
        {
            const string repeatPrefix = "repeat:";

            if (!value.StartsWith(repeatPrefix, StringComparison.Ordinal))
            {
                if (string.Equals(value, "token:json-special", StringComparison.Ordinal))
                    return "quote\"slash\\tab\tnewline\n";

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

            public List<IInstantValueSensor<int>> IntSensors { get; } = new List<IInstantValueSensor<int>>();
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
