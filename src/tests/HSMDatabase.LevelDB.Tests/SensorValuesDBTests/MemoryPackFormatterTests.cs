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
                new object[] { new DoubleBarValue { Max = 100, Min = 1, FirstValue = 5, LastValue = 80 } },
                new object[] { new IntegerBarValue { Max = 100, Min = 1, FirstValue = 5, LastValue = 80, StdDev = 1.41 } },
                new object[] { new DoubleBarValue { Max = 100, Min = 1, FirstValue = 5, LastValue = 80, StdDev = 0 } }
            };

        [Theory]
        [MemberData(nameof(TestData))]
        public void SerializeTest(BaseValue value)
        {
            var bytes = _formatter.Serialize(value);

            var result = _formatter.Deserialize(bytes);

            Assert.Equal(value, result);
        }

        // Rows written before #1509 (bars without StdDev), captured from the formatter at the
        // commit before the field was added. They must still read, with StdDev unknown (null) —
        // never 0 — and every other field intact.
        private const string PreStdDevIntBar = "ChMA/sI3tB/fSAD+////AQAAAGMA/sI3tB/fSAAAAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAIAAAAAKDyhLMf30gA/sI3tB/fSAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAAAACQAAAAUAAAABAAAAAgAAAAcAAAA=";
        private const string PreStdDevDoubleBar = "CxMA/sI3tB/fSAD/////AP7CN7Qf30gAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAACAAAAACg8oSzH99IAP7CN7Qf30gAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAEkAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+D8AAAAAAAAjQAAAAAAAABVAAQAAAAAAAAAAAAAAAAAEQAAAAAAAAB5A";

        [Fact]
        public void Bars_stored_before_stddev_read_with_it_unknown()
        {
            var open = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

            var intBar = Assert.IsType<IntegerBarValue>(_formatter.Deserialize(Convert.FromBase64String(PreStdDevIntBar)));
            Assert.Null(intBar.StdDev);
            Assert.Equal((1, 9, 5, 2, 7, 8), (intBar.Min, intBar.Max, intBar.Mean, intBar.FirstValue, intBar.LastValue, intBar.Count));
            Assert.Equal(open, intBar.OpenTime);
            Assert.Equal(open.AddMinutes(5), intBar.CloseTime);
            Assert.Equal("c", intBar.Comment);

            var doubleBar = Assert.IsType<DoubleBarValue>(_formatter.Deserialize(Convert.FromBase64String(PreStdDevDoubleBar)));
            Assert.Null(doubleBar.StdDev);
            Assert.Equal((1.5, 9.5, 5.25, 2.5, 7.5, 8), (doubleBar.Min, doubleBar.Max, doubleBar.Mean, doubleBar.FirstValue, doubleBar.LastValue, doubleBar.Count));
            Assert.Equal(4.5, doubleBar.EmaMean);
        }

        [Fact]
        public void Bar_stddev_round_trips()
        {
            var bar = new DoubleBarValue { Min = 1, Max = 9, Mean = 5, Count = 8, StdDev = 2.25 };

            var result = Assert.IsType<DoubleBarValue>(_formatter.Deserialize(_formatter.Serialize(bar)));

            Assert.Equal(2.25, result.StdDev);
        }
    }
}
