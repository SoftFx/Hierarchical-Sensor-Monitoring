using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Model;
using HSMDatabase.AccessManager.Formatters;
using HSMServer.Core.Tests.DatabaseTests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.SensorValuesDBTests;

// #1720: the merge primitive of sensor move, and the newest-value lookup the
// merge relies on.
[Collection("Database collection")]
public class SensorValuesMergeTests : DatabaseCoreTestsBase<SensorValuesMergeFixture>, IClassFixture<DatabaseRegisterFixture>
{
    private static readonly MemoryPackFormatter _formatter = new();


    public SensorValuesMergeTests(SensorValuesMergeFixture fixture, DatabaseRegisterFixture registerFixture)
        : base(fixture, registerFixture) { }


    [Fact]
    public void GetLatestValue_ForTheSensorWhoseIdSortsLast_FindsItsNewestRow()
    {
        // The highest possible id: its key range is the last one in the database,
        // so the seek to its max key runs past the end.
        var last = new Guid(Enumerable.Repeat((byte)0xFF, 16).ToArray());
        var time = DateTime.UtcNow.AddMinutes(-5);

        Add(last, time, 1);
        Add(last, time.AddMinutes(1), 2);

        var latest = _databaseCoreManager.DatabaseCore.GetLatestValue(last, DateTime.MaxValue.Ticks);

        Assert.NotNull(latest);
        Assert.Equal(2, ((IntegerValue)_formatter.Deserialize(latest)).Value);
    }

    [Fact]
    public void MergeSensorValues_CopiesRows_AndKeepsTheTargetsRowOnASameTickCollision()
    {
        var from = Guid.NewGuid();
        var into = Guid.NewGuid();
        var time = DateTime.UtcNow.AddMinutes(-10);

        Add(into, time, 10);
        Add(from, time, 20);
        Add(from, time.AddMinutes(1), 21);
        Add(from, time.AddMinutes(2), 22);

        var (copied, collisions) = _databaseCoreManager.DatabaseCore.MergeSensorValues(from, into);

        Assert.Equal(2, copied);
        Assert.Equal(1, collisions);
        Assert.Equal([10, 21, 22], Read(into));
        Assert.Equal([20, 21, 22], Read(from)); // the source is left in place
    }


    private void Add(Guid sensorId, DateTime time, int value) =>
        _databaseCoreManager.DatabaseCore.AddSensorValue(sensorId, new IntegerValue { Time = time, Value = value });

    private List<int> Read(Guid sensorId) =>
        _databaseCoreManager.DatabaseCore.GetSensorValues(sensorId, DateTime.MinValue, DateTime.MaxValue)
            .ToBlockingEnumerable()
            .Select(bytes => (IntegerValue)_formatter.Deserialize(bytes))
            .OrderBy(v => v.Time)
            .Select(v => v.Value)
            .ToList();
}

public class SensorValuesMergeFixture : DatabaseFixture
{
    protected override string DatabaseFolder => nameof(SensorValuesMergeTests);
}
