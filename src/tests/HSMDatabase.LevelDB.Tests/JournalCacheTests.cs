using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
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
    private IJournalService _journalService;
    
    public JournalCacheTests(TreeValuesCacheFixture fixture, DatabaseRegisterFixture registerFixture)
        : base(fixture, registerFixture)
    {
        _journalService = new JournalService(_databaseCoreManager.DatabaseCore);
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

        var journals = await _journalService.GetJournalValuesPage(expectedProduct.Id, DateTime.MinValue, DateTime.MaxValue, RecordType.Changes, 1).Flatten();
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
            TTL = new TimeIntervalModel(321123213123321)
        };
        
        _valuesCache.UpdateProduct(productUpdate);
        var journals = await _journalService.GetJournalValuesPage(expectedProduct.Id, DateTime.MinValue, DateTime.MaxValue, RecordType.Changes, MaxHistoryCount).Flatten();
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
            var journals = await _journalService.GetJournalValuesPage(sensor.Id, DateTime.MinValue, DateTime.MaxValue, RecordType.Changes, MaxHistoryCount).Flatten();
           
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

            _journalService.AddJournals(sensor, updating);
            sensor.Update(updating);
            sensors.Add(sensor);
        }

        return sensors;
    }
}