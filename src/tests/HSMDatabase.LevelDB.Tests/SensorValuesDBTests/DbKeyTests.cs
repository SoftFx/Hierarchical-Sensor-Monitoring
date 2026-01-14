using HSMDatabase.AccessManager;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.SensorValuesDBTests;

public class DbKeyTests
{

    [Fact]
    public void CreateKeyTest()
    {
        var guid = Guid.NewGuid();
        var timestamp = DateTime.UtcNow.Ticks;

        DbKey dbKey = new DbKey(guid, timestamp);

        var bytes = dbKey.ToBytes();

        var dbKey1 = DbKey.FromBytes(bytes);

        Assert.Equal(dbKey, dbKey1);
    }


    [Theory]
    [InlineData("01-13-2026 10:10:00", "01-12-2026 00:00:00")]
    [InlineData("01-18-2026 23:59:59", "01-12-2026 00:00:00")]
    public void GetStartOfWeekTest(string actual, string expected)
    {
        var expectedDateTime = DateTime.Parse(expected);
        var actualDateTime = DateTime.Parse(actual);

        var min = DateTimeMethods.GetStartOfWeek(actualDateTime);

        Assert.Equal(expectedDateTime, min);
    }

    [Theory]
    [InlineData("01-13-2026 10:10:00", "01-19-2026 00:00:00")]
    [InlineData("01-18-2026 23:59:59", "01-19-2026 00:00:00")]
    public void GetEndOfWeekTest(string actual, string expected)
    {
        var expectedDateTime = DateTime.Parse(expected);
        var actualDateTime = DateTime.Parse(actual);

        var min = DateTimeMethods.GetEndOfWeek(actualDateTime);

        Assert.Equal(expectedDateTime, min);
    }

}