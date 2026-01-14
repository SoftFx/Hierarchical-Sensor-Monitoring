using Xunit;
using HSMCommon.Model;
using HSMDatabase.AccessManager.Formatters;
using System.Text;

namespace HSMDatabase.LevelDB.Tests.SensorValuesDBTests
{
    public class MemoryPackFormatterTests
    {
        private readonly MemoryPackFormatter _formatter = new MemoryPackFormatter();

        public static IEnumerable<object[]> TestData =>
            new List<object[]>
            {
                new object[] { new BooleanValue { Value = true} },
                new object[] { new IntegerValue { Value = 1 } },
                new object[] { new DoubleValue { Value = 0.22 } },
                new object[] { new StringValue { Value = "Value"} },
                //new object[] { new FileValue { Value = UTF8Encoding.UTF8.GetBytes("File") } },
                new object[] { new RateValue { Value = 1 } },
                new object[] { new IntegerBarValue { Max = 100, Min = 1, FirstValue = 5, LastValue = 80 } },
                new object[] { new DoubleBarValue { Max = 100, Min = 1, FirstValue = 5, LastValue = 80 } }
            };

        [Theory]
        [MemberData(nameof(TestData))]
        public void SerializeTest(BaseValue value)
        {
            var bytes = _formatter.Serialize(value);

            var result = _formatter.Deserialize(bytes);

            Assert.Equal(value, result);
        }

    }
}
