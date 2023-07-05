using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
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

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public async Task SensorUpdateTest(int n)
    {
        var sensors = GetUpdatedSensors(n);
        foreach (var sensor in sensors)
        {
            var journals = await _journalService.GetJournalValuesPage(sensor.Id, DateTime.MinValue, DateTime.MaxValue, RecordType.Actions, MaxHistoryCount).Flatten();
           
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