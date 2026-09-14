using System;
using System.Linq;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using SensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;
using HSMServer.Model.ManagementApi.SensorTree;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi
{
    // The wire shapes of the sensor-tree read surface (#1386): the typed value
    // envelope for every sensor type (file content never serialized), unit
    // resolution, and the enum-options mapping. These are the payloads an
    // OpenAPI-driven agent parses — pin them type by type.
    public class SensorTreeDtoMapperTests
    {
        private static readonly DateTime Time = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);


        private static BaseSensorModel BuildSensor(SensorType type, Func<SensorEntity, SensorEntity> customize = null)
        {
            var entity = EntitiesFactory.BuildSensorEntity(type: (byte)type);
            return SensorModelFactory.Build(customize is null ? entity : customize(entity));
        }

        private static object ValueOf(BaseValue value) =>
            SensorTreeDtoMapper.ToValueDto(BuildSensor(SensorType.Integer), value).Value;


        [Fact]
        public void ToValueDto_NullValue_IsNull()
        {
            Assert.Null(SensorTreeDtoMapper.ToValueDto(BuildSensor(SensorType.Integer), null));
        }


        [Fact]
        public void ToValueDto_Scalars_MapNatively()
        {
            Assert.Equal(true, ValueOf(new BooleanValue { Value = true, Time = Time }));
            Assert.Equal(42, ValueOf(new IntegerValue { Value = 42, Time = Time }));
            Assert.Equal(1.5, ValueOf(new DoubleValue { Value = 1.5, Time = Time }));
            Assert.Equal("up", ValueOf(new StringValue { Value = "up", Time = Time }));
            Assert.Equal("7.02:03:04", ValueOf(new TimeSpanValue { Value = new TimeSpan(7, 2, 3, 4), Time = Time }));
            Assert.Equal("1.2.3.4", ValueOf(new VersionValue { Value = new Version(1, 2, 3, 4), Time = Time }));
            Assert.Equal(3.25, ValueOf(new RateValue { Value = 3.25, Time = Time }));
        }


        [Fact]
        public void ToValueDto_Enum_MapsValueAndLabelFromSensorOptions()
        {
            var sensor = BuildSensor(SensorType.Enum, entity => entity with
            {
                EnumOptions = new System.Collections.Generic.Dictionary<int, EnumOptionEntity>
                {
                    [2] = new() { Value = "Degraded", Description = "link degraded" },
                },
            });

            var dto = SensorTreeDtoMapper.ToValueDto(sensor, new EnumValue { Value = 2, Time = Time });
            var typed = Assert.IsType<EnumValueDto>(dto.Value);

            Assert.Equal((2, "Degraded"), (typed.Value, typed.Label));

            // An unregistered option maps to a null label, not a failure.
            var unregistered = Assert.IsType<EnumValueDto>(
                SensorTreeDtoMapper.ToValueDto(sensor, new EnumValue { Value = 7, Time = Time }).Value);
            Assert.Equal((7, null), (unregistered.Value, unregistered.Label));
        }


        [Fact]
        public void ToValueDto_Bars_MapStatistics()
        {
            var intBar = SensorTreeDtoMapper.ToValueDto(BuildSensor(SensorType.IntegerBar),
                new IntegerBarValue { Min = 1, Max = 9, Mean = 5, Count = 4, Time = Time }).Value;
            Assert.Equal((1d, 9d, 5d, 4), (Assert.IsType<BarValueDto>(intBar).Min,
                Assert.IsType<BarValueDto>(intBar).Max, Assert.IsType<BarValueDto>(intBar).Mean,
                Assert.IsType<BarValueDto>(intBar).Count));

            var doubleBar = SensorTreeDtoMapper.ToValueDto(BuildSensor(SensorType.DoubleBar),
                new DoubleBarValue { Min = 0.5, Max = 2.5, Mean = 1.5, Count = 8, Time = Time }).Value;
            Assert.Equal((0.5, 2.5, 1.5, 8), (Assert.IsType<BarValueDto>(doubleBar).Min,
                Assert.IsType<BarValueDto>(doubleBar).Max, Assert.IsType<BarValueDto>(doubleBar).Mean,
                Assert.IsType<BarValueDto>(doubleBar).Count));
        }


        [Fact]
        public void ToValueDto_File_MapsMetadataOnly_ContentNeverSerialized()
        {
            var dto = SensorTreeDtoMapper.ToValueDto(BuildSensor(SensorType.File),
                new FileValue { Value = [0xDE, 0xAD, 0xBE, 0xEF], Name = "log", Extension = ".txt", OriginalSize = 4, Time = Time });

            var typed = Assert.IsType<FileValueDto>(dto.Value);
            Assert.Equal(("log", ".txt", 4L), (typed.Name, typed.Extension, typed.Size));
            Assert.IsNotType<byte[]>(dto.Value);
        }


        [Fact]
        public void ToValueDto_EnvelopeCarriesTimeStatusComment_UtcPinned()
        {
            // BaseValue.Time normalizes through ToUniversalTime on set, so the
            // honest input is an explicitly-UTC instant; the mapper must keep the
            // Kind pinned (never an Unspecified that would serialize without Z).
            var utc = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

            var dto = SensorTreeDtoMapper.ToValueDto(BuildSensor(SensorType.Integer),
                new IntegerValue { Value = 1, Time = utc, Status = SensorStatus.Error, Comment = "note" });

            Assert.Equal(utc, dto.Time);
            Assert.Equal(DateTimeKind.Utc, dto.Time.Kind);
            Assert.Equal("Error", dto.Status);
            Assert.Equal("note", dto.Comment);
        }


        [Fact]
        public void ToSensorDto_Units_OriginalUnitThenRateDenominator()
        {
            var percent = BuildSensor(SensorType.Double, entity => entity with { OriginalUnit = (int)Unit.Percents });
            Assert.Equal("%", SensorTreeDtoMapper.ToSensorDto(percent).Unit);

            var rate = BuildSensor(SensorType.Rate);
            Assert.Equal("# per sec", SensorTreeDtoMapper.ToSensorDto(rate).Unit);

            var plain = BuildSensor(SensorType.Integer);
            Assert.Null(SensorTreeDtoMapper.ToSensorDto(plain).Unit);
        }


        [Fact]
        public void ToSensorDto_EnumOptionsMapped_OrderedAndTyped_OthersNull()
        {
            var enumSensor = BuildSensor(SensorType.Enum, entity => entity with
            {
                EnumOptions = new System.Collections.Generic.Dictionary<int, EnumOptionEntity>
                {
                    [3] = new() { Value = "Down", Description = "link down" },
                    [1] = new() { Value = "Up", Description = "link up" },
                },
            });

            var options = SensorTreeDtoMapper.ToSensorDto(enumSensor).EnumOptions;

            Assert.Equal([(1, "Up", "link up"), (3, "Down", "link down")],
                options.Select(o => (o.Value, o.Label, o.Description)));

            Assert.Null(SensorTreeDtoMapper.ToSensorDto(BuildSensor(SensorType.Integer)).EnumOptions);
        }
    }
}
