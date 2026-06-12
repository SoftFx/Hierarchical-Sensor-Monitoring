using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        // Meta-suite ("test the tests", #1094): every fixture under meta/ carries a deliberately
        // wrong expectation, and a correct driver MUST fail it. A driver that passes one is not
        // evaluating the assertion it claims to evaluate. A crash is not detection — the case
        // must surface as a thrown assertion/contract failure.
        [Theory]
        [MemberData(nameof(CollectorMetaMustFailCases))]
        public async Task Meta_must_fail_fixture_is_rejected_by_the_driver(string caseName, IReadOnlyList<ContractStep> steps)
        {
            Assert.False(string.IsNullOrWhiteSpace(caseName));

            var state = new ContractState();
            Exception detected = null;

            try
            {
                foreach (var step in steps)
                    await ExecuteStepAsync(state, step).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                detected = ex;
            }
            finally
            {
                if (state.Collector != null)
                    await state.Collector.Stop().ConfigureAwait(false);

                state.Collector?.Dispose();
            }

            Assert.True(detected != null, $"Must-fail fixture '{caseName}' passed — the driver is not detecting wrong expectations.");
        }

        public static IEnumerable<object[]> CollectorMetaMustFailCases()
        {
            foreach (var contractFile in FindMetaContractFiles())
            {
                var contractName = Path.GetFileNameWithoutExtension(contractFile);
                var cases = ReadContractCases(contractFile);

                // Drivers abort a fixture on the first failing step, so a second case in a
                // must-fail fixture would never be proven — one mutation per file.
                if (cases.Count != 1)
                    throw new InvalidOperationException($"Meta fixture '{contractName}' must contain exactly one case.");

                foreach (var testCase in cases)
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

                case "create_int_sensor_with_options":
                    AddSensor(state, state.IntSensors, state.Collector.CreateIntSensor(
                        ExpandTextToken(step.Arg(0)),
                        new InstantSensorOptions
                        {
                            TTL = long.Parse(step.Arg(1)) > 0 ? TimeSpan.FromMilliseconds(long.Parse(step.Arg(1))) : (TimeSpan?)null,
                            SensorUnit = int.Parse(step.Arg(2)) >= 0 ? (Unit)int.Parse(step.Arg(2)) : (Unit?)null,
                            Description = ExpandTextToken(step.Arg(3)),
                        }));
                    break;

                case "create_enum_sensor_with_options":
                    AddSensor(state, state.EnumSensors, state.Collector.CreateEnumSensor(
                        ExpandTextToken(step.Arg(0)),
                        new EnumSensorOptions
                        {
                            Description = ExpandTextToken(step.Arg(1)),
                            EnumOptions = ParseEnumOptions(step.Arg(2)),
                        }));
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

                case "expect_registration_count":
                    var registrationTimeout = step.TryArg(1, out var registrationSeconds)
                        ? TimeSpan.FromSeconds(int.Parse(registrationSeconds))
                        : TimeSpan.FromSeconds(2);

                    Assert.True(
                        await state.Sender.WaitForRegistrationCountAsync(int.Parse(step.Arg(0)), registrationTimeout).ConfigureAwait(false),
                        $"Expected {step.Arg(0)} registration(s), got {state.Sender.Registrations.Count}.");
                    break;

                case "expect_registration_contains":
                    Assert.Contains(step.Arg(1), RegistrationText(state.Sender.Registrations[int.Parse(step.Arg(0))]));
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

                case "create_collector_with_limits":
                    state.Sender = new RecordingSender();
                    state.Collector = CreateCollector(
                        state.Sender,
                        maxQueueSize: int.Parse(step.Arg(0)),
                        maxValuesInPackage: int.Parse(step.Arg(1)),
                        collectPeriodMs: int.Parse(step.Arg(2)));
                    break;

                case "create_int_bar_sensor":
                    AddSensor(state, state.IntBarSensors, state.Collector.CreateIntBarSensor(
                        step.Arg(0),
                        BuildConformanceBarOptions(int.Parse(step.Arg(1)), int.Parse(step.Arg(2)))));
                    break;

                case "create_double_bar_sensor":
                    AddSensor(state, state.DoubleBarSensors, state.Collector.CreateDoubleBarSensor(
                        step.Arg(0),
                        BuildConformanceBarOptions(int.Parse(step.Arg(1)), int.Parse(step.Arg(2)), int.Parse(step.Arg(3)))));
                    break;

                case "add_bar_int":
                    state.IntBarSensors[int.Parse(step.Arg(0))].AddValue(int.Parse(step.Arg(1)));
                    break;

                case "add_bar_double":
                    state.DoubleBarSensors[int.Parse(step.Arg(0))].AddValue(ParseDouble(step.Arg(1)));
                    break;

                case "add_bar_int_sequence":
                    AddBarIntSequence(
                        state,
                        barIndex: int.Parse(step.Arg(0)),
                        count: int.Parse(step.Arg(1)),
                        startValue: int.Parse(step.Arg(2)),
                        valueStep: int.Parse(step.Arg(3)));
                    break;

                case "add_bar_int_parallel":
                    await AddBarIntParallelAsync(
                        state,
                        barIndex: int.Parse(step.Arg(0)),
                        workerCount: int.Parse(step.Arg(1)),
                        valuesPerWorker: int.Parse(step.Arg(2)),
                        startValue: int.Parse(step.Arg(3))).ConfigureAwait(false);
                    break;

                case "add_int_bar_partial":
                    state.IntBarSensors[int.Parse(step.Arg(0))].AddPartial(
                        min: int.Parse(step.Arg(1)),
                        max: int.Parse(step.Arg(2)),
                        mean: int.Parse(step.Arg(3)),
                        first: int.Parse(step.Arg(4)),
                        last: int.Parse(step.Arg(5)),
                        count: int.Parse(step.Arg(6)));
                    break;

                case "add_double_bar_partial":
                    state.DoubleBarSensors[int.Parse(step.Arg(0))].AddPartial(
                        min: ParseDouble(step.Arg(1)),
                        max: ParseDouble(step.Arg(2)),
                        mean: ParseDouble(step.Arg(3)),
                        first: ParseDouble(step.Arg(4)),
                        last: ParseDouble(step.Arg(5)),
                        count: int.Parse(step.Arg(6)));
                    break;

                case "sleep_ms":
                    await Task.Delay(int.Parse(step.Arg(0))).ConfigureAwait(false);
                    break;

                case "set_sender_fail_next":
                    state.Sender.FailNext(int.Parse(step.Arg(0)));
                    break;

                case "set_sender_hang":
                    state.Sender.HangSends();
                    break;

                case "stop_expect_under_ms":
                    var stopTimer = Stopwatch.StartNew();
                    await state.Collector.Stop().ConfigureAwait(false);
                    stopTimer.Stop();

                    Assert.True(
                        stopTimer.ElapsedMilliseconds < long.Parse(step.Arg(0)),
                        $"Stop took {stopTimer.ElapsedMilliseconds} ms, expected under {step.Arg(0)} ms.");
                    break;

                case "expect_bar_field":
                    ExpectBarField(state, int.Parse(step.Arg(0)), step.Arg(1), step.Arg(2));
                    break;

                case "expect_bar_open_close_aligned":
                    ExpectBarAligned(BarAt(state, int.Parse(step.Arg(0))), long.Parse(step.Arg(1)));
                    break;

                case "expect_all_bars_aligned":
                    ExpectAllBarsAligned(state, long.Parse(step.Arg(0)));
                    break;

                case "expect_bar_open_times_increasing":
                    ExpectBarOpenTimesIncreasing(state);
                    break;

                case "expect_bar_count_total":
                    Assert.Equal(
                        int.Parse(step.Arg(0)),
                        state.Sender.Values.OfType<BarSensorValueBase>().Sum(bar => bar.Count));
                    break;

                case "expect_sent_count_between":
                    await ExpectSentCountBetweenAsync(
                        state,
                        min: int.Parse(step.Arg(0)),
                        max: int.Parse(step.Arg(1)),
                        timeout: TimeSpan.FromSeconds(int.Parse(step.Arg(2)))).ConfigureAwait(false);
                    break;

                case "expect_each_value_once":
                    ExpectEachValueOnce(state, startValue: int.Parse(step.Arg(0)), count: int.Parse(step.Arg(1)));
                    break;

                case "create_rate_sensor":
                    AddSensor(state, state.RateSensors, state.Collector.CreateRateSensor(
                        step.Arg(0),
                        new RateSensorOptions { PostDataPeriod = TimeSpan.FromMilliseconds(int.Parse(step.Arg(1))) }));
                    break;

                case "add_rate":
                    state.RateSensors[int.Parse(step.Arg(0))]
                        .AddValue(ParseDouble(step.Arg(1)), ParseStatus(step.Arg(2)), ExpandTextToken(step.Arg(3)));
                    break;

                case "add_rate_raw":
                    state.RateSensors[int.Parse(step.Arg(0))]
                        .AddValue(ParseDouble(step.Arg(1)), ParseRawStatus(step.Arg(2)), ExpandTextToken(step.Arg(3)));
                    break;

                case "create_function_int_sensor":
                    var functionConstant = int.Parse(step.Arg(2));
                    AddSensor(state, state.FunctionSensors, state.Collector.CreateFunctionSensor(
                        step.Arg(0),
                        () => functionConstant,
                        new FunctionSensorOptions { PostDataPeriod = TimeSpan.FromMilliseconds(int.Parse(step.Arg(1))) }));
                    break;

                case "create_values_function_int_sum_sensor":
                    AddSensor(state, state.ValuesFunctionSensors, state.Collector.CreateValuesFunctionSensor<int, int>(
                        step.Arg(0),
                        values => values.Sum(),
                        new ValuesFunctionSensorOptions
                        {
                            PostDataPeriod = TimeSpan.FromMilliseconds(int.Parse(step.Arg(1))),
                            MaxCacheSize = int.Parse(step.Arg(2)),
                        }));
                    break;

                case "add_function_value":
                    state.ValuesFunctionSensors[int.Parse(step.Arg(0))].AddValue(int.Parse(step.Arg(1)));
                    break;

                case "create_file_sensor":
                    AddSensor(state, state.FileSensors, state.Collector.CreateFileSensor(
                        step.Arg(0),
                        new FileSensorOptions { DefaultFileName = step.Arg(1), Extension = step.Arg(2) }));
                    break;

                case "add_file_value":
                    state.FileSensors[int.Parse(step.Arg(0))]
                        .AddValue(ExpandTextToken(step.Arg(1)), ParseStatus(step.Arg(2)), ExpandTextToken(step.Arg(3)));
                    break;

                case "expect_eventually_payload_contains":
                    await ExpectEventuallyPayloadContainsAsync(
                        state,
                        substring: step.Arg(0),
                        timeout: TimeSpan.FromSeconds(int.Parse(step.Arg(1)))).ConfigureAwait(false);
                    break;

                case "expect_eventually_value_above":
                    await ExpectEventuallyValueAboveAsync(
                        state,
                        threshold: ParseDouble(step.Arg(0)),
                        timeout: TimeSpan.FromSeconds(int.Parse(step.Arg(1)))).ConfigureAwait(false);
                    break;

                case "expect_no_new_payloads_for_ms":
                    var payloadBaseline = state.Sender.Values.Count;
                    await Task.Delay(int.Parse(step.Arg(0))).ConfigureAwait(false);
                    Assert.Equal(payloadBaseline, state.Sender.Values.Count);
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
            int port = 443,
            int maxQueueSize = 20000,
            int maxValuesInPackage = 50,
            int collectPeriodMs = 20)
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
                MaxQueueSize = maxQueueSize,
                MaxValuesInPackage = maxValuesInPackage,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(collectPeriodMs),
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMilliseconds(100),
                MaxDeduplicatedMessages = 100,
            });
        }

        private static BarSensorOptions BuildConformanceBarOptions(int barPeriodMs, int postPeriodMs, int precision = 2)
        {
            // BarTickPeriod (and PostDataPeriod when post_period_ms = 0) are pinned a year out so the
            // only bar publishes a fixture can observe are the roll-on-add inside AddValue/AddPartial
            // and the partial-bar flush on Stop — no background CheckCurrentBar/send-loop ticks.
            var inert = TimeSpan.FromDays(365);

            return new BarSensorOptions
            {
                BarPeriod = TimeSpan.FromMilliseconds(barPeriodMs),
                PostDataPeriod = postPeriodMs <= 0 ? inert : TimeSpan.FromMilliseconds(postPeriodMs),
                BarTickPeriod = inert,
                Precision = precision,
            };
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

        private static void AddBarIntSequence(ContractState state, int barIndex, int count, int startValue, int valueStep)
        {
            var sensor = state.IntBarSensors[barIndex];

            for (var i = 0; i < count; i++)
                sensor.AddValue(startValue + i * valueStep);
        }

        private static Task AddBarIntParallelAsync(ContractState state, int barIndex, int workerCount, int valuesPerWorker, int startValue)
        {
            var sensor = state.IntBarSensors[barIndex];

            var tasks = Enumerable.Range(0, workerCount)
                .Select(worker => Task.Run(() =>
                {
                    for (var valueIndex = 0; valueIndex < valuesPerWorker; valueIndex++)
                        sensor.AddValue(startValue + worker * valuesPerWorker + valueIndex);
                }))
                .ToArray();

            return Task.WhenAll(tasks);
        }

        private static BarSensorValueBase BarAt(ContractState state, int payloadIndex)
        {
            var values = state.Sender.Values;
            var resolved = payloadIndex < 0 ? values.Count + payloadIndex : payloadIndex;

            return Assert.IsAssignableFrom<BarSensorValueBase>(values[resolved]);
        }

        private static void ExpectBarField(ContractState state, int payloadIndex, string field, string expected)
        {
            var bar = BarAt(state, payloadIndex);

            switch (field)
            {
                case "type":
                    Assert.Equal(int.Parse(expected), (int)bar.Type);
                    return;
                case "status":
                    Assert.Equal(int.Parse(expected), (int)bar.Status);
                    return;
                case "count":
                    Assert.Equal(int.Parse(expected), bar.Count);
                    return;
            }

            var actual = GetBarNumericField(bar, field);
            var expectedValue = ParseDouble(expected);

            // Relative tolerance sidesteps formatting differences between language harnesses
            // (C# "R" round-trip vs C++ max-precision printing); type/count/status stay exact.
            var tolerance = Math.Max(1e-12, Math.Abs(expectedValue) * 1e-9);

            Assert.True(
                Math.Abs(actual - expectedValue) <= tolerance,
                $"Bar field '{field}': expected {expectedValue}, got {actual}.");
        }

        private static double GetBarNumericField(BarSensorValueBase bar, string field)
        {
            if (bar is BarSensorValueBase<int> intBar)
            {
                switch (field)
                {
                    case "min": return intBar.Min;
                    case "max": return intBar.Max;
                    case "mean": return intBar.Mean;
                    case "first": return intBar.FirstValue ?? double.NaN;
                    case "last": return intBar.LastValue;
                }
            }
            else if (bar is BarSensorValueBase<double> doubleBar)
            {
                switch (field)
                {
                    case "min": return doubleBar.Min;
                    case "max": return doubleBar.Max;
                    case "mean": return doubleBar.Mean;
                    case "first": return doubleBar.FirstValue ?? double.NaN;
                    case "last": return doubleBar.LastValue;
                }
            }

            throw new InvalidOperationException($"Unsupported bar field '{field}' for payload type '{bar.GetType().Name}'.");
        }

        private static long ToUnixMs(DateTime time) => (time.Ticks - 621355968000000000L) / 10000L;

        private static void ExpectBarAligned(BarSensorValueBase bar, long periodMs)
        {
            var openMs = ToUnixMs(bar.OpenTime);
            var closeMs = ToUnixMs(bar.CloseTime);

            Assert.Equal(periodMs, closeMs - openMs);
            Assert.Equal(0, openMs % periodMs);
        }

        private static void ExpectAllBarsAligned(ContractState state, long periodMs)
        {
            var bars = state.Sender.Values.OfType<BarSensorValueBase>().ToArray();

            Assert.NotEmpty(bars);

            foreach (var bar in bars)
                ExpectBarAligned(bar, periodMs);
        }

        private static void ExpectBarOpenTimesIncreasing(ContractState state)
        {
            var bars = state.Sender.Values.OfType<BarSensorValueBase>().ToArray();

            for (var i = 1; i < bars.Length; i++)
                Assert.True(
                    bars[i - 1].OpenTime < bars[i].OpenTime,
                    $"Bar open times must be strictly increasing, got {bars[i - 1].OpenTime:O} then {bars[i].OpenTime:O}.");
        }

        private static async Task ExpectSentCountBetweenAsync(ContractState state, int min, int max, TimeSpan timeout)
        {
            Assert.True(
                await state.Sender.WaitForAtLeastAsync(min, timeout).ConfigureAwait(false),
                $"Expected at least {min} sent value(s), got {state.Sender.Values.Count}.");

            var count = state.Sender.Values.Count;

            Assert.True(count <= max, $"Expected at most {max} sent value(s), got {count}.");
        }

        private static void ExpectEachValueOnce(ContractState state, int startValue, int count)
        {
            var values = state.Sender.Values.OfType<IntSensorValue>().Select(value => value.Value).ToList();

            Assert.Equal(count, values.Count);

            var distinct = new HashSet<int>(values);

            Assert.Equal(count, distinct.Count);

            for (var value = startValue; value < startValue + count; value++)
                Assert.Contains(value, distinct);
        }

        private static async Task ExpectEventuallyPayloadContainsAsync(ContractState state, string substring, TimeSpan timeout)
        {
            var stopAt = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < stopAt)
            {
                if (state.Sender.Values.Any(value => PayloadText(value).Contains(substring)))
                    return;

                await Task.Delay(10).ConfigureAwait(false);
            }

            Assert.True(false, $"No payload contained '{substring}' within {timeout}.");
        }

        // Scans numeric payload values (rate/double/int) — used where the exact value is timing
        // dependent (rate = sum / measured elapsed) and the portable contract is only "the
        // accumulated sum eventually shows up as a positive rate".
        private static async Task ExpectEventuallyValueAboveAsync(ContractState state, double threshold, TimeSpan timeout)
        {
            var stopAt = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < stopAt)
            {
                if (state.Sender.Values.Any(value => TryGetNumericValue(value, out var numeric) && numeric > threshold))
                    return;

                await Task.Delay(10).ConfigureAwait(false);
            }

            Assert.True(false, $"No payload value above {threshold} within {timeout}.");
        }

        private static bool TryGetNumericValue(SensorValueBase value, out double numeric)
        {
            if (value is RateSensorValue rateValue)
            {
                numeric = rateValue.Value;
                return true;
            }

            if (value is DoubleSensorValue doubleValue)
            {
                numeric = doubleValue.Value;
                return true;
            }

            if (value is IntSensorValue intValue)
            {
                numeric = intValue.Value;
                return true;
            }

            numeric = 0;
            return false;
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

        // Canonical cross-language registration (AddOrUpdateSensorRequest) text — fixed field
        // order; the portable subset only (alerts and managed-only flags are asserted by
        // language-local tests). TTLTicks are .NET ticks (ttl_ms * 10000).
        private static string RegistrationText(AddOrUpdateSensorRequest request)
        {
            var ttls = request.TTLs == null
                ? "null"
                : "[" + string.Join(",", request.TTLs.Select(t => t?.ToString(CultureInfo.InvariantCulture) ?? "null")) + "]";

            var enums = request.EnumOptions == null
                ? "null"
                : "[" + string.Join(",", request.EnumOptions.Select(o =>
                    $"{{\"Key\":{o.Key},\"Value\":\"{EscapeJson(o.Value)}\",\"Color\":{o.Color},\"Description\":\"{EscapeJson(o.Description)}\"}}")) + "]";

            return "{" +
                   "\"Command\":\"AddOrUpdate\"," +
                   $"\"Path\":\"{EscapeJson(request.Path)}\"," +
                   $"\"SensorType\":{(request.SensorType.HasValue ? ((int)request.SensorType.Value).ToString(CultureInfo.InvariantCulture) : "null")}," +
                   $"\"TTLTicks\":{ttls}," +
                   $"\"OriginalUnit\":{(request.OriginalUnit.HasValue ? ((int)request.OriginalUnit.Value).ToString(CultureInfo.InvariantCulture) : "null")}," +
                   $"\"Description\":{(request.Description == null ? "null" : "\"" + EscapeJson(request.Description) + "\"")}," +
                   $"\"EnumOptions\":{enums}" +
                   "}";
        }

        // "key:value:color:description;key:value:color:description;..." — values must not
        // contain ':' or ';' (fixture authoring constraint).
        private static List<EnumOption> ParseEnumOptions(string text)
        {
            var options = new List<EnumOption>();

            foreach (var part in text.Split(';'))
            {
                var fields = part.Split(':');
                if (fields.Length != 4)
                    throw new InvalidOperationException($"Invalid enum option '{part}' — expected key:value:color:description.");

                options.Add(new EnumOption(
                    int.Parse(fields[0], CultureInfo.InvariantCulture),
                    fields[1],
                    fields[3],
                    int.Parse(fields[2], CultureInfo.InvariantCulture)));
            }

            return options;
        }

        private static string PayloadText(SensorValueBase value)
        {
            if (value is BarSensorValueBase bar)
                return BarPayloadText(bar);

            if (value is FileSensorValue file)
                return FilePayloadText(file);

            return "{" +
                   $"\"Type\":{(int)value.Type}," +
                   $"\"Path\":\"{EscapeJson(value.Path)}\"," +
                   $"\"Value\":{PayloadValueText(value)}," +
                   $"\"Status\":{(int)value.Status}," +
                   $"\"Comment\":\"{EscapeJson(value.Comment)}\"" +
                   "}";
        }

        // Canonical cross-language bar payload text — field order and formatting are part of the
        // conformance contract (the C++ harness emits the same shape).
        private static string BarPayloadText(BarSensorValueBase bar)
        {
            string min, max, mean, first, last;

            if (bar is BarSensorValueBase<int> intBar)
            {
                min = intBar.Min.ToString(CultureInfo.InvariantCulture);
                max = intBar.Max.ToString(CultureInfo.InvariantCulture);
                mean = intBar.Mean.ToString(CultureInfo.InvariantCulture);
                first = intBar.FirstValue?.ToString(CultureInfo.InvariantCulture) ?? "null";
                last = intBar.LastValue.ToString(CultureInfo.InvariantCulture);
            }
            else if (bar is BarSensorValueBase<double> doubleBar)
            {
                min = doubleBar.Min.ToString("R", CultureInfo.InvariantCulture);
                max = doubleBar.Max.ToString("R", CultureInfo.InvariantCulture);
                mean = doubleBar.Mean.ToString("R", CultureInfo.InvariantCulture);
                first = doubleBar.FirstValue?.ToString("R", CultureInfo.InvariantCulture) ?? "null";
                last = doubleBar.LastValue.ToString("R", CultureInfo.InvariantCulture);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported conformance bar payload type '{bar.GetType().FullName}'.");
            }

            return "{" +
                   $"\"Type\":{(int)bar.Type}," +
                   $"\"Path\":\"{EscapeJson(bar.Path)}\"," +
                   $"\"Min\":{min}," +
                   $"\"Max\":{max}," +
                   $"\"Mean\":{mean}," +
                   $"\"First\":{first}," +
                   $"\"Last\":{last}," +
                   $"\"Count\":{bar.Count}," +
                   $"\"OpenTimeMs\":{ToUnixMs(bar.OpenTime)}," +
                   $"\"CloseTimeMs\":{ToUnixMs(bar.CloseTime)}," +
                   $"\"Status\":{(int)bar.Status}," +
                   $"\"Comment\":\"{EscapeJson(bar.Comment)}\"" +
                   "}";
        }

        // Canonical cross-language file payload — content is asserted as UTF-8 text.
        private static string FilePayloadText(FileSensorValue file)
        {
            var content = file.Value == null ? string.Empty : Encoding.UTF8.GetString(file.Value.ToArray());

            return "{" +
                   $"\"Type\":{(int)file.Type}," +
                   $"\"Path\":\"{EscapeJson(file.Path)}\"," +
                   $"\"Value\":\"{EscapeJson(content)}\"," +
                   $"\"Name\":\"{EscapeJson(file.Name)}\"," +
                   $"\"Extension\":\"{EscapeJson(file.Extension)}\"," +
                   $"\"Status\":{(int)file.Status}," +
                   $"\"Comment\":\"{EscapeJson(file.Comment)}\"" +
                   "}";
        }

        private static string PayloadValueText(SensorValueBase value)
        {
            if (value is BoolSensorValue boolValue)
                return boolValue.Value ? "true" : "false";

            if (value is RateSensorValue rateValue)
                return rateValue.Value.ToString("R", CultureInfo.InvariantCulture);

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

        private static IReadOnlyList<string> FindContractFiles() =>
            Directory.GetFiles(FindContractDirectory(), "*.hsmtest").OrderBy(x => x, StringComparer.Ordinal).ToArray();

        private static IReadOnlyList<string> FindMetaContractFiles() =>
            Directory.GetFiles(Path.Combine(FindContractDirectory(), "meta"), "*.hsmtest").OrderBy(x => x, StringComparer.Ordinal).ToArray();

        private static string FindContractDirectory()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "tests", "conformance", "collector");
                if (Directory.Exists(candidate))
                    return candidate;

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

            public List<IBarSensor<int>> IntBarSensors { get; } = new List<IBarSensor<int>>();

            public List<IBarSensor<double>> DoubleBarSensors { get; } = new List<IBarSensor<double>>();

            public List<IMonitoringRateSensor> RateSensors { get; } = new List<IMonitoringRateSensor>();

            public List<INoParamsFuncSensor<int>> FunctionSensors { get; } = new List<INoParamsFuncSensor<int>>();

            public List<IParamsFuncSensor<int, int>> ValuesFunctionSensors { get; } = new List<IParamsFuncSensor<int, int>>();

            public List<IFileSensor> FileSensors { get; } = new List<IFileSensor>();
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

            private int _failNext;

            private int _hangSends;

            public void Dispose() { }

            // Each charged token makes one subsequent data-send attempt throw BEFORE anything is
            // recorded — the queue's re-enqueue/retry loop is what's under test. Priority sends do
            // not consume tokens. Failure must be an exception: the dispatch layer treats a default
            // PackageSendingInfo (Error == null) as success.
            public void FailNext(int count) => Interlocked.Add(ref _failNext, count);

            // Models a dead/black-holed transport: every data send blocks until the caller's
            // cancellation token fires (like the real HTTP client against an unreachable server).
            // Nothing is recorded — the bounded stop must give up on these sends, not wait them out.
            public void HangSends() => Volatile.Write(ref _hangSends, 1);

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public async ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                if (Volatile.Read(ref _hangSends) == 1)
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);

                if (TryConsumeFailToken())
                    throw new InvalidOperationException("Conformance: injected send failure.");

                lock (_lock)
                    _values.AddRange(items);

                return default;
            }

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                lock (_lock)
                    _values.AddRange(items);

                return default;
            }

            // Sensor registrations (AddOrUpdateSensorRequest) are part of the recorded contract
            // (registration_contract.hsmtest); they do not consume fail/hang injection.
            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token)
            {
                lock (_lock)
                {
                    foreach (var command in commands)
                        if (command is AddOrUpdateSensorRequest registration)
                            _registrations.Add(registration);
                }

                return default;
            }

            public IReadOnlyList<AddOrUpdateSensorRequest> Registrations
            {
                get
                {
                    lock (_lock)
                        return _registrations.ToList();
                }
            }

            public async Task<bool> WaitForRegistrationCountAsync(int count, TimeSpan timeout)
            {
                var stopAt = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < stopAt)
                {
                    if (Registrations.Count == count)
                        return true;

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return Registrations.Count == count;
            }

            private readonly List<AddOrUpdateSensorRequest> _registrations = new List<AddOrUpdateSensorRequest>();

            // File payloads are part of the recorded contract (file_contract.hsmtest); they do not
            // consume fail/hang injection — those fixtures target the data queue only.
            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token)
            {
                lock (_lock)
                    _values.Add(file);

                return default;
            }

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

            public async Task<bool> WaitForAtLeastAsync(int count, TimeSpan timeout)
            {
                var stopAt = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < stopAt)
                {
                    if (Values.Count >= count)
                        return true;

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return Values.Count >= count;
            }

            private bool TryConsumeFailToken()
            {
                while (true)
                {
                    var current = Volatile.Read(ref _failNext);

                    if (current <= 0)
                        return false;

                    if (Interlocked.CompareExchange(ref _failNext, current - 1, current) == current)
                        return true;
                }
            }
        }
    }
}
