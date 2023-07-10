using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Journal;
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
            var journals = await _journalService.GetJournalValuesPage(new(sensor.Id, DateTime.MinValue, DateTime.MaxValue, RecordType.Actions, MaxHistoryCount)).Flatten();
           
            Assert.NotEmpty(journals);
        }
    }

    [Theory]
    [InlineData(5)]
    public async Task BorderTest(int n)
    {
        var id = Guid.NewGuid();
        var journals = new List<JournalRecordModel>();
        for (int i = 0; i < n; i++)
        {
            string value = RandomGenerator.GetRandomString();
            var journal = new JournalRecordModel(id, new DateTime(RandomGenerator.GetRandomTimeSpan(3155368608000005611, 3155362560000005611).Ticks), value);
            journals.Add(journal);
            _journalService.AddJournal(journal);
        }

        await Task.Delay(1000);
        var expected = journals.OrderByDescending(x => x.Key.Time).ToList();
        var actual = new List<JournalRecordModel>();
        actual = await _journalService.GetJournalValuesPage(new(id, DateTime.MinValue, DateTime.MaxValue, RecordType.Actions, 50000)).Flatten();

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(expected[i].Value, actual[i].Value);
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
        }

        return sensors;
    }

    [Theory]
    [InlineData(1000)]
    public void CHeckKeys(int n)
    {
        for (int i = 0; i < n; i++)
        {
            var id = Guid.NewGuid();
            var value = RandomGenerator.GetRandomString();
            var record = new JournalRecordModel(id, new DateTime(RandomGenerator.GetRandomTimeSpan(DateTime.MaxValue.Ticks, DateTime.MinValue.Ticks).Ticks), value);

            var key = record.Key;

            var desKey = JournalKey.FromBytes(key.GetBytes());
            
            Assert.Equal(key.Id, desKey.Id);
            Assert.Equal(key.Time, desKey.Time);
            Assert.Equal(key.Type, desKey.Type);
        }
    }
}