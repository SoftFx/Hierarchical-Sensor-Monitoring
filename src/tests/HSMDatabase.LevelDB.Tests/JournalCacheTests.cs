using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests;
using Xunit;

namespace HSMDatabase.LevelDB.Tests;

public class JournalCacheTests : MonitoringCoreTestsBase<TreeValuesCacheFixture>, IClassFixture<DatabaseRegisterFixture>
{
    public JournalCacheTests(TreeValuesCacheFixture fixture, DatabaseRegisterFixture registerFixture)
        : base(fixture, registerFixture)
    {
    }

    [Fact]
    public async Task SingleJournalUpdateTest()
    {
        var expectedProduct = _valuesCache.GetProducts().First();
        
        var productUpdate = new ProductUpdate()
        {
            Id = expectedProduct.Id, 
            Description = "qweqwe"
        };
        
        _valuesCache.UpdateProduct(productUpdate);

        var journals = await _valuesCache.GetJournalValuesPage(expectedProduct.Id, DateTime.MinValue, DateTime.MaxValue, JournalType.Changes, 1).Flatten();
        Assert.Single(journals);
    }
    
    [Fact]
    public async Task JournalUpdateTest()
    {
        var expectedProduct = _valuesCache.GetProducts().First();
        
        var productUpdate = new ProductUpdate()
        {
            Id = expectedProduct.Id, 
            Description = "qweqwe",
            ExpectedUpdateInterval = new TimeIntervalModel(321123213123321)
        };
        
        _valuesCache.UpdateProduct(productUpdate);
        var journals = await _valuesCache.GetJournalValuesPage(expectedProduct.Id, DateTime.MinValue, DateTime.MaxValue, JournalType.Changes, MaxHistoryCount).Flatten();
        Assert.NotEmpty(journals);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public async Task SensorUpdateTest(int n)
    {
        var sensors = GetUpdatedSensors(n);
        foreach (var sensor in sensors)
        {
            var journals = await _valuesCache.GetJournalValuesPage(sensor.Id, DateTime.MinValue, DateTime.MaxValue, JournalType.Changes, MaxHistoryCount).Flatten();
           
            Assert.NotEmpty(journals);
        }
    }

    private List<BaseSensorModel> GetUpdatedSensors(int n)
    {
        var sensors = new List<BaseSensorModel>();
        for (int i = 0; i < n; i++)
        {
            var updating = SensorModelFactory.BuildSensorUpdate();
            var sensor = SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity());

            sensor.Update(updating);
            sensors.Add(sensor);
            foreach (var journal in sensor.JournalRecordModels)
            {
                _valuesCache.AddJournal(new Key(journal.Id, journal.Time, JournalType.Changes), journal.ToJournalEntity());
            }
        }

        return sensors;
    }
}