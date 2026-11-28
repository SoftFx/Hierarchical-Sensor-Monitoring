using System.Globalization;
using System.Text;
using LevelDB;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.StorageTests;

// #1791: the guard every write of a LevelDB database goes through. A failed native write is stood for by an exception the
// test throws in the guard (a real one needs a full disk: LevelDbDiskFullTests); the roll that closes the log is the real
// one, made to fail by a directory in the place of the next log file, which LevelDB cannot create (on every platform).
public sealed class LevelDbWriteGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-1791-{Guid.NewGuid():N}");


    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }


    [Fact]
    public void AfterAFailedWrite_NoWriteIsTakenUntilANewLogIsStarted_ThenTheWritesAreDurable()
    {
        var path = Path.Combine(_root, "db");
        var faults = new LevelDbWriteFaults();

        using (var db = Open(path, faults, out var guard))
        {
            db.GuardedPut(Key(0), Value(0));

            var blocker = BlockNextLog(path);
            FailNextWrite(guard);

            Assert.Throws<LevelDBException>(() => db.GuardedPut(Key(1), Value(1)));
            Assert.True(guard.IsRefusingWrites);
            Assert.Equal(1, faults.Failures);
            Assert.Equal(new[] { Path.GetFullPath(path) }, faults.RefusingDatabases);

            // The roll fails (no new log can be created): refused, and nothing of the write reaches the database.
            Assert.Throws<LevelDbWritesRefusedException>(() => db.GuardedPut(Key(2), Value(2)));
            Assert.Throws<LevelDbWritesRefusedException>(() => db.GuardedDelete(Key(0)));
            Assert.True(guard.IsRefusingWrites);
            Assert.Equal(2, faults.RefusedWrites);
            Assert.Null(db.Get(Key(2)));
            Assert.NotNull(db.Get(Key(0)));

            // A new log can be created: the next write starts it, the old one is closed, and the write is taken.
            Directory.Delete(blocker);
            db.GuardedPut(Key(3), Value(3));

            Assert.False(guard.IsRefusingWrites);
            Assert.Empty(faults.RefusingDatabases);
            Assert.All(LogNumbers(path), number => Assert.True(number > 3, $"log {number} is still there"));
            Assert.Equal(1, faults.Failures);
        }

        using (var db = new DB(path, new Options()))
        {
            Assert.NotNull(db.Get(Key(0)));
            Assert.Null(db.Get(Key(1)));
            Assert.Null(db.Get(Key(2)));
            Assert.Equal(Value(3), db.Get(Key(3)));
        }
    }

    [Fact]
    public void WhileTheWritesAreRefused_TheRollIsTriedAtMostOncePerRecoveryInterval()
    {
        var path = Path.Combine(_root, "db");

        using var db = Open(path, new LevelDbWriteFaults(), out var guard, TimeSpan.FromHours(1));

        db.GuardedPut(Key(0), Value(0));

        var blocker = BlockNextLog(path);
        FailNextWrite(guard);

        Assert.Throws<LevelDBException>(() => db.GuardedPut(Key(1), Value(1))); // the roll right after it fails

        Directory.Delete(blocker);

        // The next try is an hour away: still refused, although a new log could be created now.
        Assert.Throws<LevelDbWritesRefusedException>(() => db.GuardedPut(Key(2), Value(2)));

        guard.RecoveryInterval = TimeSpan.Zero;
        db.GuardedPut(Key(2), Value(2));

        Assert.False(guard.IsRefusingWrites);
    }

    [Fact]
    public void ARollRightAfterTheFailure_TakesTheNextWriteAtOnce()
    {
        var path = Path.Combine(_root, "db");

        using var db = Open(path, new LevelDbWriteFaults(), out var guard, TimeSpan.FromHours(1));

        db.GuardedPut(Key(0), Value(0));
        FailNextWrite(guard);

        Assert.Throws<LevelDBException>(() => db.GuardedPut(Key(1), Value(1)));

        // Nothing blocks the roll: the failure itself closed the log.
        Assert.False(guard.IsRefusingWrites);
        db.GuardedPut(Key(2), Value(2));
        Assert.Equal(Value(2), db.Get(Key(2)));
    }

    // Every write of the adapter goes through the guard of its database, the writes on its native database too.
    [Theory]
    [InlineData("Put")]
    [InlineData("PutSynced")]
    [InlineData("Delete")]
    [InlineData("DeleteSynced")]
    [InlineData("PutBatch")]
    [InlineData("DeleteStartingWithBefore")]
    [InlineData("DeleteValueFromTo")]
    [InlineData("Native.GuardedWrite")]
    public void EveryWriteOfTheAdapter_IsRefusedAfterAFailedWrite(string write)
    {
        using var db = new LevelDBDatabaseAdapter(Path.Combine(_root, write));

        db.Put(Key(0), Value(0));

        db.Writes.BeforeRecovery = () => throw new IOException("no new log in this test");
        FailNextWrite(db.Writes);

        Assert.ThrowsAny<ServerDatabaseException>(() => db.Put(Key(1), Value(1)));
        Assert.True(db.Writes.IsRefusingWrites);

        Action run = write switch
        {
            "Put" => () => db.Put(Key(2), Value(2)),
            "PutSynced" => () => db.PutSynced(Key(2), Value(2)),
            "Delete" => () => db.Delete(Key(0)),
            "DeleteSynced" => () => db.DeleteSynced(Key(0)),
            "PutBatch" => () => db.PutBatch([(Key(2), Value(2))], [Key(0)]),
            "DeleteStartingWithBefore" => () => db.DeleteStartingWithBefore(Encoding.ASCII.GetBytes("key-"), Encoding.ASCII.GetBytes("key-9"), 10),
            "DeleteValueFromTo" => () => db.DeleteValueFromTo(Key(0), Key(9)),
            "Native.GuardedWrite" => () =>
            {
                using var batch = new WriteBatch();
                batch.Put(Key(2), Value(2));
                db.Native.GuardedWrite(batch, new WriteOptions { Sync = true });
            },
            _ => throw new ArgumentOutOfRangeException(nameof(write)),
        };

        var refused = Assert.ThrowsAny<ServerDatabaseException>(run);

        Assert.IsType<LevelDbWritesRefusedException>(refused is LevelDbWritesRefusedException ? refused : refused.InnerException);
        Assert.True(db.TryRead(Key(0), out _));
        Assert.False(db.TryRead(Key(2), out _));
    }

    [Fact]
    public void AWriteOnAClosedDatabase_IsNotAWriteFailure()
    {
        var faults = new LevelDbWriteFaults();
        var db = Open(Path.Combine(_root, "db"), faults, out var guard);

        db.Dispose();

        Assert.Throws<ObjectDisposedException>(() => db.GuardedPut(Key(0), Value(0)));
        Assert.False(guard.IsRefusingWrites);
        Assert.Equal(0, faults.Failures);
    }

    [Fact]
    public void ADatabaseClosedWhileItRefusesWrites_LeavesTheList()
    {
        var path = Path.Combine(_root, "db");
        var faults = new LevelDbWriteFaults();
        var db = Open(path, faults, out var guard);

        guard.BeforeRecovery = () => throw new IOException("no new log in this test");
        FailNextWrite(guard);
        Assert.Throws<LevelDBException>(() => db.GuardedPut(Key(1), Value(1)));
        Assert.Single(faults.RefusingDatabases);

        db.Dispose(); // the next open recovers the log

        Assert.Empty(faults.RefusingDatabases);
        Assert.Equal(1, faults.Failures);
    }

    // The databases of the table stores are opened by LevelDbOpen, which registers them: their guard knows the path.
    [Fact]
    public void ADatabaseOpenedByLevelDbOpen_HasAGuardWithItsPath()
    {
        var path = Path.Combine(_root, "table");
        Directory.CreateDirectory(path);

        using var db = LevelDbOpen.Open(path, new Options { CreateIfMissing = true });

        Assert.Equal(Path.GetFullPath(path), LevelDbWriteGuard.Of(db).Path);
    }

    // A database opened without registration (none in the server: only a test or a tool) never takes writes again after a
    // failed one, because its guard cannot see its logs.
    [Fact]
    public void ADatabaseOpenedWithoutRegistration_RefusesWritesForGoodAfterAFailure()
    {
        var path = Path.Combine(_root, "raw");
        Directory.CreateDirectory(path);

        using var db = new DB(path, new Options { CreateIfMissing = true });
        var guard = LevelDbWriteGuard.Of(db);

        guard.RecoveryInterval = TimeSpan.Zero;
        FailNextWrite(guard);

        Assert.Null(guard.Path);
        Assert.Throws<LevelDBException>(() => db.GuardedPut(Key(1), Value(1)));
        Assert.Throws<LevelDbWritesRefusedException>(() => db.GuardedPut(Key(2), Value(2)));
    }


    private static DB Open(string path, LevelDbWriteFaults faults, out LevelDbWriteGuard guard, TimeSpan? recoveryInterval = null)
    {
        Directory.CreateDirectory(path);

        var db = new DB(path, new Options { CreateIfMissing = true });

        guard = LevelDbWriteGuard.Register(db, path, faults);
        guard.RecoveryInterval = recoveryInterval ?? TimeSpan.Zero;

        return db;
    }

    // The next native write of the guard fails, as a write on a full disk does.
    private static void FailNextWrite(LevelDbWriteGuard guard)
    {
        guard.BeforeWrite = () =>
        {
            guard.BeforeWrite = null;
            throw new LevelDBException("IO error: 000003.log: No space left on device (injected by the test)");
        };
    }

    // A directory under the name of the next file LevelDB numbers: the new log of a roll cannot be created there.
    private static string BlockNextLog(string path)
    {
        var next = Directory.EnumerateFiles(path)
                            .Select(file => Path.GetFileName(file).Split('.', '-'))
                            .SelectMany(parts => parts)
                            .Select(part => long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0)
                            .Max() + 1;

        var blocker = Path.Combine(path, $"{next:D6}.log");
        Directory.CreateDirectory(blocker);

        return blocker;
    }

    private static IEnumerable<long> LogNumbers(string path) =>
        Directory.EnumerateFiles(path, "*.log").Select(file => long.Parse(Path.GetFileNameWithoutExtension(file), CultureInfo.InvariantCulture));

    private static byte[] Key(int i) => Encoding.ASCII.GetBytes($"key-{i}");

    private static byte[] Value(int i) => Encoding.ASCII.GetBytes($"value-{i}");
}
