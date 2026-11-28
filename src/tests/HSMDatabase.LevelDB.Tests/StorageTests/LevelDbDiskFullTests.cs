using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace HSMDatabase.LevelDB.Tests.StorageTests;

// #1791 on a real full disk. A write that fails because the volume is full can leave a torn record in the write-ahead
// log; LevelDB appended the next writes after it, they succeeded, and the recovery of the next open dropped them with the
// rest of the torn record's block. The contract: every write a database acknowledged survives a reopen.
//
// Needs HSM_TEST_SMALL_VOLUME: a directory on a small file system (a few MB) that the test may fill, such as a tmpfs
// (CI: the disk-full job of server-tests.yml; locally: docker run --tmpfs /vol:size=4m -e HSM_TEST_SMALL_VOLUME=/vol ...,
// see aicontext/features/storage/write-faults/feature.md). Skipped without it.
public sealed class LevelDbDiskFullTests(ITestOutputHelper output) : IDisposable
{
    private const string VolumeVariable = "HSM_TEST_SMALL_VOLUME";

    private static readonly string Volume = Environment.GetEnvironmentVariable(VolumeVariable);

    private readonly string _root = Volume is null ? null : Path.Combine(Volume, $"hsm-1791-{Guid.NewGuid():N}");


    public void Dispose()
    {
        if (_root is null)
            return;

        try { Directory.Delete(_root, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }


    [SmallVolumeTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryWriteAcknowledgedAroundADiskFullEpisode_SurvivesAReopen(bool synced)
    {
        var path = Path.Combine(_root, "EnvironmentData");
        var acknowledged = new List<int>();
        var failedWhileFull = 0;

        using (var db = new LevelDBDatabaseAdapter(path))
        {
            for (int i = 0; i < 5; i++)
                Write(db, i, synced, acknowledged);

            using (Fill())
            {
                for (int i = 100; i < 110; i++)
                    if (!Write(db, i, synced, acknowledged))
                        failedWhileFull++;
            }

            // The space is back: these are the writes #1791 lost (200 on the wire, gone after a restart).
            for (int i = 200; i < 210; i++)
                Write(db, i, synced, acknowledged);
        }

        Assert.True(failedWhileFull > 0, "no write failed on the full volume: the scenario did not happen");

        using (var db = new LevelDBDatabaseAdapter(path))
        {
            var lost = acknowledged.Where(i => !db.TryRead(Key(i), out var value) || !value.AsSpan().SequenceEqual(Value(i))).ToList();

            output.WriteLine($"acknowledged {acknowledged.Count}: {string.Join(",", acknowledged)}");
            output.WriteLine($"LevelDB LOG of the reopen:{Environment.NewLine}{File.ReadAllText(Path.Combine(path, "LOG"))}");

            Assert.True(lost.Count == 0, $"acknowledged, then lost by the reopen: {string.Join(",", lost)}");
        }
    }

    // While the volume is full no new log is started (its memtable flush would fail and LevelDB would then refuse every
    // write until a reopen); once the space is back, the next write starts one, and the writes from then on are taken
    // without a restart and survive a reopen.
    [SmallVolumeTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnceTheSpaceIsBack_WritesAreTakenAgainWithoutARestart_AndSurviveAReopen(bool synced)
    {
        var path = Path.Combine(_root, "EnvironmentData");
        var acknowledged = new List<int>();
        var refusedWhileFull = 0;

        using (var db = new LevelDBDatabaseAdapter(path))
        {
            db.Writes.RecoveryInterval = TimeSpan.Zero; // every refused write tries the roll

            for (int i = 0; i < 5; i++)
                Write(db, i, synced, acknowledged);

            using (Fill())
            {
                for (int i = 100; i < 110; i++)
                    if (!Write(db, i, synced, acknowledged))
                        refusedWhileFull++;

                Assert.True(db.Writes.IsRefusingWrites, "no write failed on the full volume: the scenario did not happen");
            }

            for (int i = 200; i < 210; i++)
                Assert.True(Write(db, i, synced, acknowledged), $"{i}: refused although the space is back");

            Assert.False(db.Writes.IsRefusingWrites);
        }

        Assert.True(refusedWhileFull > 0);

        using (var db = new LevelDBDatabaseAdapter(path))
        {
            var lost = acknowledged.Where(i => !db.TryRead(Key(i), out var value) || !value.AsSpan().SequenceEqual(Value(i))).ToList();

            Assert.True(lost.Count == 0, $"acknowledged, then lost by the reopen: {string.Join(",", lost)}");
        }
    }

    [SmallVolumeFact]
    public void AfterTheSpaceIsBack_TheReopenedDatabaseTakesWritesAgain()
    {
        var path = Path.Combine(_root, "SensorValues");
        var acknowledged = new List<int>();

        using (var db = new LevelDBDatabaseAdapter(path))
        {
            Write(db, 0, false, acknowledged);

            using (Fill())
            {
                for (int i = 100; i < 110; i++)
                    Write(db, i, false, acknowledged);
            }
        }

        // The restart.
        using (var db = new LevelDBDatabaseAdapter(path))
        {
            db.Put(Key(300), Value(300));

            Assert.True(db.TryRead(Key(300), out _));
            Assert.All(acknowledged, i => Assert.True(db.TryRead(Key(i), out _), $"{i} acknowledged and lost"));
        }
    }


    private bool Write(LevelDBDatabaseAdapter db, int i, bool synced, List<int> acknowledged)
    {
        try
        {
            if (synced)
                db.PutSynced(Key(i), Value(i));
            else
                db.Put(Key(i), Value(i));

            acknowledged.Add(i);
            output.WriteLine($"put {i}: ok");
            return true;
        }
        catch (ServerDatabaseException e)
        {
            output.WriteLine($"put {i}: {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    // Fills the volume to its last byte; disposing frees the space.
    private Filler Fill()
    {
        var file = Path.Combine(Volume, $"fill-{Guid.NewGuid():N}.bin");
        long size = 0;

        using (var stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1))
        {
            foreach (var chunk in new[] { 1 << 16, 1 << 12, 1 << 9, 1 })
            {
                var bytes = new byte[chunk];

                try
                {
                    while (true)
                    {
                        stream.Write(bytes);
                        stream.Flush();
                        size += chunk;
                    }
                }
                catch (IOException)
                {
                    // The next, smaller chunk takes what is left.
                }
            }
        }

        output.WriteLine($"volume filled with {size} bytes");

        return new Filler(file);
    }

    private sealed class Filler(string file) : IDisposable
    {
        public void Dispose() => File.Delete(file);
    }

    private static byte[] Key(int i) => Encoding.ASCII.GetBytes($"key-{i:D4}");

    private static byte[] Value(int i) => Encoding.ASCII.GetBytes($"value-{i:D4}-" + new string('x', 900));


    private sealed class SmallVolumeFactAttribute : FactAttribute
    {
        public SmallVolumeFactAttribute()
        {
            if (Volume is null)
                Skip = $"needs {VolumeVariable}: a directory on a small file system the test may fill (#1791)";
        }
    }

    private sealed class SmallVolumeTheoryAttribute : TheoryAttribute
    {
        public SmallVolumeTheoryAttribute()
        {
            if (Volume is null)
                Skip = $"needs {VolumeVariable}: a directory on a small file system the test may fill (#1791)";
        }
    }
}
