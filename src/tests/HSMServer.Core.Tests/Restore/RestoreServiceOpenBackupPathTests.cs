using System;
using System.IO;
using System.Linq;
using HSMDatabase.AccessManager;
using HSMDatabase.Settings;
using HSMServer.Core.Restore;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Restore;

// RestoreService.OpenBackup is the security boundary of the wizard. These tests pin its
// behaviour against path-traversal and naming attacks without spinning up a real LevelDB —
// the validation throws before any DB access, so a fake ITreeValuesCache is enough.
public sealed class RestoreServiceOpenBackupPathTests : IDisposable
{
    private sealed class TestDatabaseSettings : IDatabaseSettings
    {
        public string DatabaseBackupsFolder { get; init; }
        public string DatabaseRestoreTempFolder { get; init; }
        public string DatabaseFolder { get; init; }
        public string JournalFolder { get; init; }
        public string ExportFolder { get; init; }
        public string JournalValuesDatabaseName { get; init; }
        public string SensorValuesDatabaseName { get; init; }
        public string ServerLayoutDatabaseName { get; init; }
        public string EnvironmentDatabaseName { get; init; } = "EnvironmentData";
        public string SnaphotsDatabaseName { get; init; }
        public string PathToServerLayoutDb { get; init; }
        public string PathToEnvironmentDb { get; init; }
        public string PathToSnaphotsDb { get; init; }
        public string PathToJournalDb { get; init; }
        public string PathToExport { get; init; }
    }

    private readonly string _root;
    private readonly string _backupsFolder;
    private readonly string _tempFolder;
    private readonly RestoreService _service;

    public RestoreServiceOpenBackupPathTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"hsm-restore-open-{Guid.NewGuid():N}");
        _backupsFolder = Path.Combine(_root, "backups");
        _tempFolder = Path.Combine(_root, "tmp");
        Directory.CreateDirectory(_backupsFolder);
        Directory.CreateDirectory(_tempFolder);

        var settings = new TestDatabaseSettings
        {
            DatabaseBackupsFolder = _backupsFolder,
            DatabaseRestoreTempFolder = _tempFolder,
        };
        _service = new RestoreService(settings, Mock.Of<HSMServer.Core.Cache.ITreeValuesCache>());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* test cleanup */ }
    }

    private void CreateBackupFile(string name)
    {
        // Touch a fake file under the backups folder. OpenBackup will fail later when it tries
        // to extract a non-zip, but for traversal tests we only care that the prefix check
        // rejects the name BEFORE any I/O happens.
        File.WriteAllText(Path.Combine(_backupsFolder, name), "not a real zip");
    }

    [Theory]
    [InlineData("../outside.zip")]
    [InlineData("..\\outside.zip")]
    [InlineData("../somewhere/EnvironmentData_evil.zip")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/System32/calc.zip")]
    public void OpenBackup_RejectsTraversalAndAbsolutePaths(string fileName)
    {
        // The validator must reject these before any filesystem mutation. We assert that the
        // throw happens AND that no temp folder is created as a side effect.
        Assert.ThrowsAny<Exception>(() => _service.OpenBackup(fileName));

        Assert.Empty(Directory.GetDirectories(_tempFolder));
    }

    [Fact]
    public void OpenBackup_RejectsEmptyFileName()
    {
        Assert.Throws<ArgumentException>(() => _service.OpenBackup(""));
        Assert.Throws<ArgumentException>(() => _service.OpenBackup("   "));
    }

    [Fact]
    public void OpenBackup_RejectsWrongPrefix()
    {
        CreateBackupFile("ServerLayout_01.01.2026T00.00.zip"); // not EnvironmentData_*

        Assert.ThrowsAny<Exception>(() => _service.OpenBackup("ServerLayout_01.01.2026T00.00.zip"));
    }

    [Fact]
    public void OpenBackup_RejectsMissingFile()
    {
        // Right prefix, but the file doesn't exist. Must throw, not silently open an empty DB.
        Assert.Throws<FileNotFoundException>(() => _service.OpenBackup("EnvironmentData_never_created.zip"));
    }
}
