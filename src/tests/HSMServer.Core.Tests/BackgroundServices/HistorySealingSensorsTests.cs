using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMServer.BackgroundServices;
using HSMServer.BackgroundServices.DatabaseServices;
using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.BackgroundServices;

// #1804: the self-monitoring of the sealing job, its configuration section, and the switch.
public sealed class HistorySealingSensorsTests
{
    private readonly Dictionary<string, object> _sensors = [];
    private readonly Mock<IDataCollector> _collector = new(MockBehavior.Strict);


    public HistorySealingSensorsTests()
    {
        _collector.Setup(c => c.CreateIntSensor(It.IsAny<string>(), It.IsAny<InstantSensorOptions>()))
                  .Returns((string path, InstantSensorOptions _) => Register<int>(path));
        _collector.Setup(c => c.CreateDoubleSensor(It.IsAny<string>(), It.IsAny<InstantSensorOptions>()))
                  .Returns((string path, InstantSensorOptions _) => Register<double>(path));
        _collector.Setup(c => c.CreateStringSensor(It.IsAny<string>(), It.IsAny<InstantSensorOptions>()))
                  .Returns((string path, InstantSensorOptions _) => Register<string>(path));
    }


    [Fact]
    public void ASealPass_IsPostedOnce_WithItsCountsReasonsAndSizes()
    {
        var sensors = new HistorySealingSensors(_collector.Object);
        var pass = new HistorySealingPassReport
        {
            Sequence = 3,
            Action = HistorySealingAction.Seal,
            Outcome = HistorySealingOutcome.Done,
            Week = "SensorValuesV2_1_2",
            RowsSealed = 531_480,
            SourceBytes = 64L << 20,
            ChunkBytes = 4L << 20,
            SensorsSealed = 480,
            VerificationFailures = 0,
            SealedWeeks = 12,
        };
        pass.LeaveAsRows("Refused:ReceivingTimeOutOfRange");
        pass.LeaveAsRows("DeletedDuringSeal");
        pass.LeaveAsRows("DeletedDuringSeal");

        sensors.SendInfo(pass);
        sensors.SendInfo(pass); // the same pass again: nothing new

        Assert.Equal(("Seal Done", SensorStatus.Ok), Single<string>(HistorySealingSensors.LastRunName) is var run ? (run.Value, run.Status) : default);
        Assert.Equal(12, Single<int>(HistorySealingSensors.SealedWeeksName).Value);
        Assert.Equal(531_480, Single<int>(HistorySealingSensors.RowsSealedName).Value);
        Assert.Equal(64.0, Single<double>(HistorySealingSensors.BytesBeforeName).Value);
        Assert.Equal(4.0, Single<double>(HistorySealingSensors.BytesAfterName).Value);

        var left = Single<int>(HistorySealingSensors.LeftAsRowsName);
        Assert.Equal(3, left.Value);
        Assert.Equal("DeletedDuringSeal: 2; Refused:ReceivingTimeOutOfRange: 1", left.Comment);

        Assert.Equal((0, SensorStatus.Ok), Single<int>(HistorySealingSensors.VerificationFailuresName) is var v ? (v.Value, v.Status) : default);
    }

    [Fact]
    public void AFailedPass_IsAnError_AndAPassWithoutSealingPostsOnlyTheRunAndTheWeekCount()
    {
        var sensors = new HistorySealingSensors(_collector.Object);

        sensors.SendInfo(new HistorySealingPassReport { Sequence = 1, Action = HistorySealingAction.RemoveSourceRows, Outcome = HistorySealingOutcome.Failed, Error = "disk full", SealedWeeks = 2 });

        var run = Single<string>(HistorySealingSensors.LastRunName);
        Assert.Equal(SensorStatus.Error, run.Status);
        Assert.Contains("disk full", run.Comment);
        Assert.Equal(2, Single<int>(HistorySealingSensors.SealedWeeksName).Value);
        Assert.Empty(Values<int>(HistorySealingSensors.RowsSealedName));
        Assert.Empty(Values<int>(HistorySealingSensors.VerificationFailuresName));
    }

    [Fact]
    public void WeeksNeedingAttention_ArePostedWithEveryPass_AsAnErrorNamingThem()
    {
        var sensors = new HistorySealingSensors(_collector.Object);
        var pass = new HistorySealingPassReport { Sequence = 1, Action = HistorySealingAction.None, Outcome = HistorySealingOutcome.NothingDue };
        pass.WeeksNeedingAttention["SensorValuesV2_1_2"] = "hsm-sealed is missing";

        sensors.SendInfo(pass);
        sensors.SendInfo(new HistorySealingPassReport { Sequence = 2, Action = HistorySealingAction.None, Outcome = HistorySealingOutcome.NothingDue });

        var values = Values<int>(HistorySealingSensors.AttentionName);
        Assert.Equal((1, SensorStatus.Error), (values[0].Value, values[0].Status));
        Assert.Contains("SensorValuesV2_1_2: hsm-sealed is missing", values[0].Comment);
        Assert.Equal((0, SensorStatus.Ok), (values[1].Value, values[1].Status));
        Assert.NotEmpty(Values<int>(HistorySealingSensors.SyncFailuresName));
    }

    [Fact]
    public void BeforeAnyPass_NoPassSensorIsPosted()
    {
        var sensors = new HistorySealingSensors(_collector.Object);

        sensors.SendInfo(null);

        Assert.Empty(Values<string>(HistorySealingSensors.LastRunName));
        Assert.Empty(Values<int>(HistorySealingSensors.SealedWeeksName));
        Assert.Empty(Values<int>(HistorySealingSensors.RowsSealedName));
    }

    // The counters are process-wide (other tests of this assembly may move them meanwhile): only their presence is checked.
    [Fact]
    public void TheStoreCounters_ArePostedOnceThereIsAPass()
    {
        var sensors = new HistorySealingSensors(_collector.Object);

        sensors.SendInfo(new HistorySealingPassReport { Sequence = 1, Action = HistorySealingAction.None, Outcome = HistorySealingOutcome.NothingDue });
        sensors.SendInfo(new HistorySealingPassReport { Sequence = 2, Action = HistorySealingAction.None, Outcome = HistorySealingOutcome.NothingDue });

        Assert.NotEmpty(Values<int>(HistorySealingSensors.ChunkReadFailuresName));
        Assert.NotEmpty(Values<int>(HistorySealingSensors.ChunkRewriteFailuresName));
        Assert.Equal(2, Values<string>(HistorySealingSensors.LastRunName).Count);
    }

    [Fact]
    public async Task SwitchOff_TheServiceNeverCallsTheDatabase()
    {
        var database = new Mock<IDatabaseCore>(MockBehavior.Strict);
        var config = new Mock<IServerConfig>();
        config.SetupGet(c => c.HistorySealing).Returns(new HistorySealingConfig());

        var service = new HistorySealingService(database.Object, config.Object);
        var action = typeof(HistorySealingService).GetMethod("ServiceActionAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        await (Task)action.Invoke(service, [CancellationToken.None]);

        database.VerifyNoOtherCalls();
    }

    [Fact]
    public void Configuration_DefaultsAreOff_AndInvalidValuesAreRefusedAtStartup()
    {
        var defaults = new HistorySealingConfig();
        defaults.Validate();

        var settings = defaults.Settings;
        Assert.False(settings.Enabled);
        Assert.Equal(TimeSpan.FromDays(7), settings.SealAfter);
        Assert.Equal(TimeSpan.FromDays(28), settings.KeepSourceRows);
        Assert.Equal(settings.WorkingHoursStartUtc, settings.WorkingHoursEndUtc); // all day

        Assert.Throws<InvalidOperationException>(() => new HistorySealingConfig { KeepSourceRowsWeeks = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new HistorySealingConfig { SealAfterDays = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new HistorySealingConfig { FoldMinRows = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new HistorySealingConfig { WorkingHoursStartUtc = "25:00" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new HistorySealingConfig { WorkingHoursEndUtc = "night" }.Validate());

        var window = new HistorySealingConfig { WorkingHoursStartUtc = "22:30", WorkingHoursEndUtc = "05:00" }.Settings;
        Assert.Equal((new TimeSpan(22, 30, 0), TimeSpan.FromHours(5)), (window.WorkingHoursStartUtc, window.WorkingHoursEndUtc));
    }


    private IInstantValueSensor<T> Register<T>(string path)
    {
        var sensor = new Recording<T>();
        _sensors[path] = sensor;
        return sensor;
    }

    private List<(T Value, SensorStatus Status, string Comment)> Values<T>(string name) => ((Recording<T>)_sensors[$"{HistorySealingSensors.NodeName}/{name}"]).Values;

    private (T Value, SensorStatus Status, string Comment) Single<T>(string name) => Assert.Single(Values<T>(name));


    public sealed class Recording<T> : IInstantValueSensor<T>
    {
        public List<(T Value, SensorStatus Status, string Comment)> Values { get; } = [];

        public void AddValue(T value) => Values.Add((value, SensorStatus.Ok, string.Empty));

        public void AddValue(T value, string comment = "") => Values.Add((value, SensorStatus.Ok, comment));

        public void AddValue(T value, SensorStatus status = SensorStatus.Ok, string comment = "") => Values.Add((value, status, comment));
    }
}
