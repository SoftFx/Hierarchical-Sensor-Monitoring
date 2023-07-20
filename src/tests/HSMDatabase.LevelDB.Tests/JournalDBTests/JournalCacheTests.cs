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
    public void CheckKeysConverting(int count)
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
    [InlineData(100, 999)]
    [InlineData(1000, 41)]
    [InlineData(10000, 324)]
    public async Task SensorUpdateTest(int sensorsCount, int valuesCount)
    {
        var sensors = new List<BaseSensorModel>(sensorsCount);

        for (int i = 0; i < sensorsCount; i++)
        {
            var sensor = SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity());
            var curCnt = 0;

            while (curCnt++ <= valuesCount)
                _journalService.AddRecord(JournalFactory.GetRecord(sensor.Id));

            sensors.Add(sensor);
        }

        await Task.Delay(1000);

        var request = new JournalHistoryRequestModel(sensors[0].Id);

        foreach (var sensor in sensors)
        {
            var journals = await _journalService.GetPages(request with { Id = sensor.Id }).Flatten();

            Assert.NotEmpty(journals);
            Assert.Equal(journals.Count, valuesCount);
        }
    }


    [Theory]
    [InlineData(1, 10)]
    [InlineData(100, 1000)]
    [InlineData(1000, 1000)]
    [InlineData(10000, 100)]
    public async Task JournalOrderTest(int sensorsCount, int valuesCount)
    {
        var journals = new Dictionary<Guid, List<JournalRecordModel>>(sensorsCount);
        var sensors = new List<BaseSensorModel>(sensorsCount);

        for (int i = 0; i < sensorsCount; i++)
        {
            var sensor = SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity());
            var list = new List<JournalRecordModel>(valuesCount);

            while (list.Count <= valuesCount)
            {
                var record = JournalFactory.GetRecord(sensor.Id);

                list.Add(record);
                _journalService.AddRecord(record);
            }

            sensors.Add(sensor);
            journals.Add(sensor.Id, list);
        }

        await Task.Delay(1000);

        foreach (var (id, records) in journals)
        {
            var expectedList = records.OrderBy(x => x.Key.Time).ToList();
            var actualList = await _journalService.GetPages(new(id)).Flatten();

            Assert.Equal(expectedList.Count, actualList.Count);

            for (int i = 0; i < expectedList.Count; i++)
            {
                var expected = expectedList[i];
                var actual = actualList[i];

                Assert.Equal(actual.Key, expected.Key);
                Assert.Equal(actual.PropertyName, expected.PropertyName);
                Assert.Equal(actual.Enviroment, expected.Enviroment);
                Assert.Equal(actual.Initiator, expected.Initiator);
                Assert.Equal(actual.NewValue, expected.NewValue);
                Assert.Equal(actual.OldValue, expected.OldValue);
                Assert.Equal(actual.Path, expected.Path);
            }
        }
    }
}