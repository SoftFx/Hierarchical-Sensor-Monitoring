using System;
using System.IO;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.LevelDB;
using HSMDatabase.LevelDB.DatabaseImplementations;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.AlertTemplatesDBTests;

// Standalone tests for the read-only adapter used by the restore flow. These do NOT use the
// shared DatabaseCore fixture — they spin up a fresh LevelDB in a temp dir, write a template,
// dispose, then reopen via ForReadOnly and assert reads work. Mirrors the restore flow's
// "open unpacked backup" step in isolation.
public class LevelDBDatabaseAdapterReadOnlyTests : IDisposable
{
    private readonly string _tempRoot;

    public LevelDBDatabaseAdapterReadOnlyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"hsm-readonly-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch { /* test cleanup — ignore leftover LOCK files */ }
    }

    [Fact]
    public void ForReadOnly_OpensExistingDbAndReadsTemplate()
    {
        var dbPath = Path.Combine(_tempRoot, "srcdb");
        Directory.CreateDirectory(dbPath);

        var id = Guid.NewGuid();
        var entity = new AlertTemplateEntity
        {
            Id = id.ToByteArray(),
            Name = "Test Template",
            SensorType = 100,
            Paths = ["*/cpu"],
        };

        // Write via the normal read/write adapter (the path the live server uses).
        using (var writer = new EnvironmentDatabaseWorker(dbPath))
        {
            writer.AddAlertTemplate(entity);
            writer.AddAlertTemplateIdToList(entity.Id);
        }

        // Reopen read-only and read back.
        using var ro = new EnvironmentDatabaseWorker(LevelDBDatabaseAdapter.ForReadOnly(dbPath));

        var ids = ro.GetAllAlertTemplatesIds();
        Assert.NotEmpty(ids);

        var read = ro.GetAlertTemplate(id.ToByteArray());
        Assert.NotNull(read);
        Assert.Equal("Test Template", read.Name);
    }

    [Fact]
    public void ForReadOnly_OnMissingPath_ThrowsRatherThanSilentlyOpening()
    {
        var missingPath = Path.Combine(_tempRoot, "does-not-exist");

        // The read-only ctor must surface a LevelDB open error rather than silently producing
        // an empty DB that "successfully" reads zero templates. Restore relies on this to fail
        // fast on a corrupt/empty unpack instead of presenting an empty template list to the user.
        // (The SoftFX wrapper may create the directory as a side effect of attempting the open;
        // what matters for correctness is that the call does NOT return a usable empty DB.)
        Assert.ThrowsAny<Exception>(() => LevelDBDatabaseAdapter.ForReadOnly(missingPath));
    }
}
