using System;
using System.Collections.Generic;
using System.IO;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDatabase.LevelDB;
using HSMSensorDataObjects;
using HSMServer.BackgroundServices;
using LevelDB;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.BackgroundServices;

// #1791: the self-monitoring of the failed and refused writes of the LevelDB databases.
public sealed class StoreWriteFaultSensorsTests : IDisposable
{
    private readonly Dictionary<string, object> _sensors = [];
    private readonly Mock<IDataCollector> _collector = new(MockBehavior.Strict);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"hsm-1791-sensors-{Guid.NewGuid():N}");
    private readonly LevelDbWriteFaults _faults = new();
    private readonly DB _db;
    private readonly LevelDbWriteGuard _guard;


    public StoreWriteFaultSensorsTests()
    {
        _collector.Setup(c => c.CreateIntSensor(It.IsAny<string>(), It.IsAny<InstantSensorOptions>()))
                  .Returns((string path, InstantSensorOptions _) => Register(path));

        Directory.CreateDirectory(_folder);
        _db = new DB(_folder, new Options { CreateIfMissing = true });
        _guard = LevelDbWriteGuard.Register(_db, _folder, _faults);
    }

    public void Dispose()
    {
        _db.Dispose();

        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }


    [Fact]
    public void A_failed_write_is_an_error_in_its_sample_and_the_database_is_listed_while_it_refuses_writes()
    {
        var sensors = new StoreWriteFaultSensors(_collector.Object, _faults);

        sensors.SendInfo();

        Assert.Equal((0, SensorStatus.Ok), Last(StoreWriteFaultSensors.FailuresName));
        Assert.Equal((0, SensorStatus.Ok), Last(StoreWriteFaultSensors.RefusingName));

        // A failed write whose log cannot be closed yet, and a write refused after it.
        _guard.BeforeRecovery = () => throw new IOException("no new log in this test");
        _guard.RecoveryInterval = TimeSpan.Zero;
        _guard.BeforeWrite = () =>
        {
            _guard.BeforeWrite = null;
            throw new LevelDBException("IO error: No space left on device (injected by the test)");
        };

        Assert.Throws<LevelDBException>(() => _db.GuardedPut([1], [1]));
        Assert.Throws<LevelDbWritesRefusedException>(() => _db.GuardedPut([2], [2]));

        sensors.SendInfo();

        Assert.Equal((1, SensorStatus.Error), Last(StoreWriteFaultSensors.FailuresName));
        Assert.Equal((1, SensorStatus.Error), Last(StoreWriteFaultSensors.RefusedName));
        Assert.Equal((1, SensorStatus.Error), Last(StoreWriteFaultSensors.RefusingName));
        Assert.Equal(Path.GetFullPath(_folder), Values(StoreWriteFaultSensors.RefusingName)[^1].Comment);

        // The log can be closed now: the next write is taken, and the next sample is Ok again.
        _guard.BeforeRecovery = null;
        _db.GuardedPut([3], [3]);

        sensors.SendInfo();

        Assert.Equal((1, SensorStatus.Ok), Last(StoreWriteFaultSensors.FailuresName));
        Assert.Equal((1, SensorStatus.Ok), Last(StoreWriteFaultSensors.RefusedName));
        Assert.Equal((0, SensorStatus.Ok), Last(StoreWriteFaultSensors.RefusingName));

        // Nothing changed: nothing is posted.
        var posted = Values(StoreWriteFaultSensors.RefusingName).Count;
        sensors.SendInfo();
        Assert.Equal(posted, Values(StoreWriteFaultSensors.RefusingName).Count);
    }


    private IInstantValueSensor<int> Register(string path)
    {
        var sensor = new Recording();
        _sensors[path] = sensor;
        return sensor;
    }

    private List<(int Value, SensorStatus Status, string Comment)> Values(string name) =>
        ((Recording)_sensors[$"{StoreWriteFaultSensors.NodeName}/{name}"]).Values;

    private (int, SensorStatus) Last(string name) => Values(name)[^1] is var last ? (last.Value, last.Status) : default;


    private sealed class Recording : IInstantValueSensor<int>
    {
        public List<(int Value, SensorStatus Status, string Comment)> Values { get; } = [];

        public void AddValue(int value) => Values.Add((value, SensorStatus.Ok, string.Empty));

        public void AddValue(int value, string comment = "") => Values.Add((value, SensorStatus.Ok, comment));

        public void AddValue(int value, SensorStatus status = SensorStatus.Ok, string comment = "") => Values.Add((value, status, comment));
    }
}
