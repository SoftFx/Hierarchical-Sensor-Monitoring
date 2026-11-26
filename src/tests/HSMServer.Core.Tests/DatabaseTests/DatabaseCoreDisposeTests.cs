using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.Settings;
using HSMCommon.Model;
using HSMServer.Core.Tests.Infrastructure;
using System;
using System.IO;
using Xunit;

namespace HSMServer.Core.Tests.DatabaseTests
{
    // DatabaseCore.Dispose closes every database it opened (#1943 follow-up): on Windows a
    // database left open keeps its files locked, so a single delete of the folder fails.
    public class DatabaseCoreDisposeTests
    {
        [Fact]
        public void Dispose_ReleasesEveryDatabase_SoTheFolderDeletesInOneAttempt()
        {
            var folder = Path.Combine(Path.GetTempPath(), $"hsm-dispose-{Guid.NewGuid():N}");

            try
            {
                var core = new DatabaseCore(new DatabaseSettings { DatabaseFolder = folder });

                core.AddProduct(EntitiesFactory.BuildProductEntity());
                core.AddSensorValue(Guid.NewGuid(), new IntegerValue { Value = 1, Time = DateTime.UtcNow });
                core.Dashboards.AddEntity(new DashboardEntity { Id = Guid.NewGuid().ToByteArray(), Name = "dispose" });
                core.ChangeSets.AddEntity(new ChangeSetEntity { Id = Guid.NewGuid().ToByteArray(), Author = "dispose" });
                // The download log of large files (ADR-0022) opens its LevelDB on the first record.
                core.DownloadLog.Put(new DownloadRecordEntity { Id = Guid.NewGuid(), TimeTicks = DateTime.UtcNow.Ticks, FileName = "dispose" });

                Assert.True(Directory.Exists(core.DatabaseSettings.PathToDownloadLogDb));

                core.Dispose();

                Directory.Delete(folder, recursive: true);

                Assert.False(Directory.Exists(folder));
            }
            finally
            {
                // Only a leftover of a failed run: the assertion above is the one attempt that counts.
                if (Directory.Exists(folder))
                    try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        [Fact]
        public void Dispose_Twice_DoesNotThrow()
        {
            var folder = Path.Combine(Path.GetTempPath(), $"hsm-dispose-{Guid.NewGuid():N}");

            try
            {
                var core = new DatabaseCore(new DatabaseSettings { DatabaseFolder = folder });

                core.Dashboards.AddEntity(new DashboardEntity { Id = Guid.NewGuid().ToByteArray(), Name = "dispose" });

                core.Dispose();
                core.Dispose();
            }
            finally
            {
                if (Directory.Exists(folder))
                    try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
