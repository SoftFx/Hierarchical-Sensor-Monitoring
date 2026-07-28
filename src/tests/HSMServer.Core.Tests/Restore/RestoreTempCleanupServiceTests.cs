using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDatabase.AccessManager;
using HSMServer.BackgroundServices.DatabaseServices;
using Xunit;

namespace HSMServer.Core.Tests.Restore;

// Standalone tests for RestoreTempCleanupService.SweepNow. The service is normally driven by
// the host lifetime; here we instantiate it directly and call StartAsync to trigger the
// startup sweep, against a temp root we control.
public sealed class RestoreTempCleanupServiceTests
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
        public string EnvironmentDatabaseName { get; init; }
        public string SnaphotsDatabaseName { get; init; }
        public string PathToServerLayoutDb { get; init; }
        public string PathToEnvironmentDb { get; init; }
        public string PathToSnaphotsDb { get; init; }
        public string PathToJournalDb { get; init; }
        public string PathToExport { get; init; }
    }

    [Fact]
    public async Task StartAsync_SweepsStaleRestoreTempFolders()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"hsm-restore-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            // Pre-create stale folders exactly like the ones a crashed restore would leave behind.
            foreach (var g in new[] { Guid.NewGuid(), Guid.NewGuid() })
                Directory.CreateDirectory(Path.Combine(tempRoot, $"restore_{g:N}"));

            var settings = new TestDatabaseSettings { DatabaseRestoreTempFolder = tempRoot };
            var service = new RestoreTempCleanupService(settings);

            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);

            var remaining = Directory.GetDirectories(tempRoot, "restore_*");
            Assert.Empty(remaining);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StartAsync_CreatesTempRootIfMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"hsm-restore-cleanup-{Guid.NewGuid():N}");

        try
        {
            Assert.False(Directory.Exists(tempRoot));

            var settings = new TestDatabaseSettings { DatabaseRestoreTempFolder = tempRoot };
            var service = new RestoreTempCleanupService(settings);

            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);

            Assert.True(Directory.Exists(tempRoot));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StartAsync_LeavesSiblingFoldersAlone()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"hsm-restore-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, "restore_abc"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "unrelated_logs"));

            var settings = new TestDatabaseSettings { DatabaseRestoreTempFolder = tempRoot };
            var service = new RestoreTempCleanupService(settings);

            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);

            Assert.False(Directory.Exists(Path.Combine(tempRoot, "restore_abc")));
            Assert.True(Directory.Exists(Path.Combine(tempRoot, "unrelated_logs")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
