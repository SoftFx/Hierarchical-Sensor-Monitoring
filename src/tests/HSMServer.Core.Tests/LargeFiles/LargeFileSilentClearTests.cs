using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.LargeFiles;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMDatabase.AccessManager.LargeFiles;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles
{
    public sealed class LargeFileSilentClearFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(LargeFileSilentClearTests);
    }


    /// <summary>
    /// A clear whose delete of rows failed without a word (#1883): the files of rows that are still there are kept.
    /// </summary>
    [Collection("Database collection")]
    public class LargeFileSilentClearTests : MonitoringCoreTestsBase<LargeFileSilentClearFixture>
    {
        private const int Piece = 65_536;

        private FailingDatabaseCore _database;
        private readonly LargeFileUploadService _service;


        public LargeFileSilentClearTests(LargeFileSilentClearFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            _service = new LargeFileUploadService(_database, _valuesCache, new LargeFileOptions { MinFreeDiskMb = 0 });
        }


        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner) => _database = new FailingDatabaseCore(inner, _ => false);


        [Fact]
        public async Task A_clear_whose_delete_failed_keeps_the_files_of_the_rows_still_there()
        {
            var product = TestProductsManager.ProductId;
            var path = $"lf{Guid.NewGuid():N}"[..12] + "/f";
            var registration = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(product, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.File,
                TypeIsExplicit = true,
            });
            Assert.True(registration.IsOk, registration.Error);

            var content = new byte[Piece];
            new Random(1).NextBytes(content);

            var begin = await _service.BeginAsync(new LargeFileBeginRequest(product, path, "a.dmp", content.Length, DateTime.UtcNow, null, Piece, Piece), default);
            await _service.AppendAsync(begin.Lease, 0, content, SHA256.HashData(content), default);
            var finish = await _service.FinishAsync(begin.Lease, SHA256.HashData(content), new StoredBytesHashChecker(), default);
            Assert.Equal(LargeFileFinishOutcome.Verified, finish.Outcome);

            Assert.True(_valuesCache.TryGetSensorByPath(product, path, out var sensor));

            _database.ClearSensorValuesSilentlyFails = true;
            Assert.True((await _valuesCache.ClearSensorHistoryAsync(new ClearHistoryRequest(sensor.Id))).IsOk);

            Assert.NotNull(_database.LargeFiles.TryGetFile(finish.FileId));
            Assert.Equal(0, _database.LargeFiles.Statistics.FilesRemoved);
            Assert.NotNull(_database.GetSensorValue(sensor.Id, finish.ValueTimeUtc.Value.Ticks));
        }
    }
}
