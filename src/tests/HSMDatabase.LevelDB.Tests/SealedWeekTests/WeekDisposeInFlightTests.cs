using HSMCommon.Model;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.LevelDB.Sealed;
using HSMDatabase.Settings;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.SealedWeekTests;

// A week closed under a running read or write is a native use-after-free in LevelDB (an iterator or a put on a
// closed database): an access violation that kills the process without a managed exception. That was the
// "Test host process crashed" of server-tests on PR #1955: a test class's DatabaseCore was disposed while the
// background history load of a TreeValuesCache still read it (#1943 follow-up). DatabaseCore.Dispose now waits
// for what runs on a week, then closes it, so its files are released when it returns.
public sealed class WeekDisposeInFlightTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HeldLongEnough = TimeSpan.FromMilliseconds(500);

    private static readonly DateTime Day = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-week-dispose-{Guid.NewGuid():N}");
    private readonly Guid _sensor = Guid.NewGuid();
    private readonly DatabaseCore _core;


    public WeekDisposeInFlightTests()
    {
        _core = new DatabaseCore(new DatabaseSettings { DatabaseFolder = _root });

        for (var i = 0; i < 10; i++)
            _core.AddSensorValue(_sensor, new IntegerValue { Value = i, Time = Day.AddMinutes(i), ReceivingTime = Day.AddMinutes(i) });
    }

    public void Dispose()
    {
        _core.Dispose();

        try { Directory.Delete(_root, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }


    [Fact]
    public async Task Dispose_WaitsForARunningRead_AndTheReadGetsItsValue()
    {
        var week = Week();
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        week.InsideRead = () =>
        {
            inside.Set();
            release.Wait(Patience);
        };

        var read = Task.Run(() => _core.GetLatestValue(_sensor, Day.AddHours(1).Ticks));
        Assert.True(inside.Wait(Patience), "the read did not start");

        week.InsideRead = null;
        var dispose = Task.Run(_core.Dispose);

        // Closing the week now would leave the read on a closed native database.
        Assert.False(dispose.Wait(HeldLongEnough), "Dispose closed the week under a running read");

        release.Set();

        Assert.NotNull(await read.WaitAsync(Patience));
        await dispose.WaitAsync(Patience);

        // Closed when Dispose returned: nothing holds a file of the folder.
        Directory.Delete(_root, recursive: true);

        // A read that starts after Dispose is refused, not run on the closed database.
        Assert.Throws<ObjectDisposedException>(() => week.GetLatest(new byte[24], _sensor.ToByteArray()));
    }

    [Fact]
    public async Task Dispose_WaitsForARunningRangeDelete()
    {
        var week = Week();
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        week.InsideRangeDelete = () =>
        {
            inside.Set();
            release.Wait(Patience);
        };

        var delete = Task.Run(() => _core.ClearSensorValues(_sensor, Day, Day.AddMinutes(4)));
        Assert.True(inside.Wait(Patience), "the delete did not start");

        var dispose = Task.Run(_core.Dispose);

        Assert.False(dispose.Wait(HeldLongEnough), "Dispose closed the week under a running delete");

        release.Set();

        await delete.WaitAsync(Patience);
        await dispose.WaitAsync(Patience);

        Directory.Delete(_root, recursive: true);
    }


    private SensorValuesWeek Week() => (SensorValuesWeek)_core.SensorValuesDatabases.Single(db => db.Contains(Day.Ticks));
}
