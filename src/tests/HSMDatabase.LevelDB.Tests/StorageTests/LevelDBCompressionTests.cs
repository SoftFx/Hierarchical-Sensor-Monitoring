using System;
using System.IO;
using System.Linq;
using HSMDatabase.LevelDB;
using Xunit;
using NativeCompression = global::LevelDB.CompressionLevel;
using NativeDb = global::LevelDB.DB;
using NativeOptions = global::LevelDB.Options;

namespace HSMDatabase.LevelDB.Tests.StorageTests;

// The two halves of the first step of #1815, on whatever platform the tests run:
// - the server reads Snappy blocks, which a Linux server before SoftFX.LevelDB.Standard 1.2.2
//   could not (its native library had no Snappy);
// - outside Windows the server still writes uncompressed blocks, so a rollback to a release on
//   the old package can read everything this release wrote.
public class LevelDBCompressionTests : IDisposable
{
    private const int Rows = 4096;
    private const int ValueSize = 1024;
    private const long RawBytes = Rows * (sizeof(int) + ValueSize);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-compression-tests-{Guid.NewGuid():N}");

    public LevelDBCompressionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* test cleanup: ignore leftover LOCK files */ }
    }

    [Fact]
    public void Server_writes_snappy_on_windows_and_uncompressed_elsewhere()
    {
        var path = Path.Combine(_root, "written-by-adapter");

        using (var db = new LevelDBDatabaseAdapter(path))
        {
            for (var i = 0; i < Rows; i++)
                db.Put(BitConverter.GetBytes(i), new byte[ValueSize]);

            db.Compact();
        }

        if (OperatingSystem.IsWindows())
            Assert.True(TableBytes(path) < RawBytes / 4, $"expected Snappy on Windows, table files are {TableBytes(path)} bytes");
        else
            Assert.True(TableBytes(path) >= RawBytes, $"expected no compression until the Snappy step of #1815, table files are {TableBytes(path)} bytes");
    }

    [Fact]
    public void Server_reads_snappy_blocks()
    {
        var path = Path.Combine(_root, "written-with-snappy");
        Directory.CreateDirectory(path);

        // A database as a Windows server, or a later Linux release, writes it.
        using (var options = new NativeOptions { CreateIfMissing = true, CompressionLevel = NativeCompression.SnappyCompression })
        using (var db = new NativeDb(path, options))
        {
            for (var i = 0; i < Rows; i++)
                db.Put(BitConverter.GetBytes(i), new byte[ValueSize]);

            db.Compact();
        }

        Assert.True(TableBytes(path) < RawBytes / 4, $"the native library did not write Snappy blocks, table files are {TableBytes(path)} bytes");

        using var adapter = new LevelDBDatabaseAdapter(path);
        var rows = adapter.GetAll().ToList();

        Assert.Equal(Rows, rows.Count);
        Assert.All(rows, row => Assert.Equal(new byte[ValueSize], row.Item2));
    }

    private static long TableBytes(string path) =>
        Directory.GetFiles(path, "*.ldb").Sum(file => new FileInfo(file).Length);
}
