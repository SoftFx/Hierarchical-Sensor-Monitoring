using System;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.LevelDB.Sealed;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    // #1803: a sealed week's unreadable chunk surfaces from GetLatestValue as SealedChunkReadException. The
    // clear-history / retention path reads the value bordering its range only to widen or narrow that range;
    // it must still delete when that read fails, or a corrupt chunk would block the delete that removes it.
    [Collection("Database collection")]
    public class SealedHistoryReadFailureTests : MonitoringCoreTestsBase<TreeValuesCacheFixture>
    {
        private FailingDatabaseCore _failing;


        public SealedHistoryReadFailureTests(TreeValuesCacheFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
        }


        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner) => _failing = new FailingDatabaseCore(inner, _ => false);


        [Fact]
        public async Task ClearHistory_OfAnAggregatingSensor_DeletesEvenWhenTheBorderingValueCannotBeRead()
        {
            var accessKey = Guid.Parse(TestProductsManager.TestProductKey.Id);
            var productId = TestProductsManager.ProductId;
            const string path = "sealed_read_failure/clear";
            var start = DateTime.UtcNow.AddHours(-2);

            for (int i = 0; i < 3; i++)
                await _valuesCache.AddSensorValueAsync(accessKey, productId, SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, start.AddMinutes(i)));

            Assert.True(_valuesCache.TryGetSensorByPath(productId, path, out var sensor));
            await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, AggregateValues = true, Initiator = InitiatorInfo.System });

            Assert.NotEmpty(_databaseCoreManager.DatabaseCore.GetSensorValues(sensor.Id, DateTime.MinValue, DateTime.MaxValue).ToBlockingEnumerable());

            _failing.FailGetLatestValue = id => id == sensor.Id ? new SealedChunkReadException("test", id, 0, "injected unreadable chunk") : null;

            await _valuesCache.ClearSensorHistoryAsync(new ClearHistoryRequest(sensor.Id));

            Assert.NotNull(sensor.HistoryClearedTo); // the clear ran to its end
            Assert.Empty(_databaseCoreManager.DatabaseCore.GetSensorValues(sensor.Id, DateTime.MinValue, DateTime.MaxValue).ToBlockingEnumerable());
        }
    }
}
