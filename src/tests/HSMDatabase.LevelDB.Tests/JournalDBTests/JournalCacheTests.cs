using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.JournalDBTests;

public sealed class JournalCacheTests : MonitoringCoreTestsBase<TreeValuesCacheFixture>, IClassFixture<DatabaseRegisterFixture>
{
    public JournalCacheTests(TreeValuesCacheFixture fixture, DatabaseRegisterFixture registerFixture) : base(fixture, registerFixture) { }


    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void CheckKeys(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            var record = JournalFactory.GetRecord(id);

            var key = record.Key;
            var desKey = JournalKey.FromBytes(key.GetBytes());

            Assert.Equal(key.Id, desKey.Id);
            Assert.Equal(key.Time, desKey.Time);
            Assert.Equal(key.Type, desKey.Type);
        }
    }


    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public async Task SensorUpdateTest(int count)
    {
        var sensors = GetUpdatedSensors(count);
        var request = new JournalHistoryRequestModel() { Id = sensors[0].Id };

        foreach (var sensor in sensors)
        {
            var journals = await _journalService.GetPages(request with { Id = sensor.Id}).Flatten();

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
            var journal = new JournalRecordModel(id, value, "TestName", "Test");
            journals.Add(journal);
            _journalService.AddRecord(journal);
        }

        await Task.Delay(1000);

        var expected = journals.OrderBy(x => x.Key.Time).ToList();
        var actual = await _journalService.GetPages(new()
        {
            Id = id,
            Types = RecordType.Changes,
        }).Flatten();

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(expected[i].OldValue, actual[i].OldValue);
        }
    }


    private List<BaseSensorModel> GetUpdatedSensors(int count)
    {
        var sensors = new List<BaseSensorModel>();

        for (int i = 0; i < count; i++)
        {
            var sensor = SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity());
            _journalService.AddRecord(new JournalRecordModel(sensor.Id, "Test message", "Test name", "Test initiator"));
            sensors.Add(sensor);
        }

        return sensors;
    }
}