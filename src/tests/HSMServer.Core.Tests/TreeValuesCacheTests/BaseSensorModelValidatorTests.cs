using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using System;
using Xunit;
using SensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public class BaseSensorModelValidatorTests : MonitoringCoreTestsBase<ValidationFixture>
    {
        private const string SensorValueIsTooLong = "The value has exceeded the length limit.";
        private const string SensorValueTypeInvalid = "Sensor value type is not {0}";
        private const string SensorValueStatusInvalid = "User data has {0} status";
        private const string UnexpectedBehaviorMessage = "Unexpected behavior or error";
        private const int DefaultMaxStringLength = 150;

        //private readonly ITreeValuesCache _valuesCache;
        //private const int TooLongSensorValuesPathPartsCount = 11;
        //private const int MaxSensorValuesPathPartsCount = 10;


        public BaseSensorModelValidatorTests(ValidationFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            //_valuesCache = new TreeValuesCache(_databaseCoreManager.DatabaseCore, _userManager, _updatesQueue);
        }


        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.File)]
        [Trait("Category", "NullBase")]
        public void NullSensorValueValidationTest(SensorType type)
        {
            var sensor = BuildSensorModel(type);

            Assert.False(sensor.TryAddValue(null, out _));
        }

        [Fact]
        [Trait("Category", "StringSensorModelWarning")]
        public void StringSensorModelWarningValidationTest()
        {
            var stringModel = BuildSensorModel(SensorType.String);
            stringModel.AddPolicy(new StringValueLengthPolicy());

            var stringBase = new StringValue 
            { 
                Value = RandomGenerator.GetRandomString(DefaultMaxStringLength + 1) 
            };

            Assert.True(stringModel.TryAddValue(stringBase, out _));
            Assert.True(stringModel.ValidationResult.IsWarning);
            Assert.Equal(SensorStatus.Warning, stringModel.ValidationResult.Result);
            Assert.Equal(stringModel.ValidationResult.Message, SensorValueIsTooLong);
        }

        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.File)]
        [Trait("Category", "InvalidType")]
        public void SensorModelInvalidTypeValidationTest(SensorType sensorType)
        {
            var sensor = BuildSensorModel(sensorType);
            var errorMessage = GetBadValueTypeMessage(sensorType);

            foreach (var invalidType in Enum.GetValues<SensorType>())
            {
                if (invalidType == sensorType)
                    break;

                Assert.False(sensor.TryAddValue(SensorValuesFactory.BuildSensorValue(invalidType), out _));
                Assert.True(sensor.ValidationResult.IsError);
                Assert.Equal(SensorStatus.Error, sensor.ValidationResult.Result);
                Assert.Equal(errorMessage, sensor.ValidationResult.Message);
            }
        }

        [Theory]
        [InlineData(SensorStatus.Unknown)]
        [InlineData(SensorStatus.Error)]
        [InlineData(SensorStatus.Warning)]
        [Trait("Category", "InvalidStatus")]
        public void SensorValueStatusValidationTest(SensorStatus status)
        {
            foreach (var sensorType in Enum.GetValues<SensorType>())
            {
                var sensor = BuildSensorModel(sensorType);
                var baseValue = SensorValuesFactory.BuildSensorValue(sensorType) with { Status = status };

                var errorMessage = status == SensorStatus.Unknown ?
                    UnexpectedBehaviorMessage
                    : string.Format(SensorValueStatusInvalid, status);

                Assert.True(sensor.TryAddValue(baseValue, out _));
                Assert.Equal(baseValue.Status, sensor.ValidationResult.Result);
                Assert.Equal(errorMessage, sensor.ValidationResult.Message);

                if (status == SensorStatus.Error)
                    Assert.True(sensor.ValidationResult.IsError);
                else if (status == SensorStatus.Warning)
                    Assert.True(sensor.ValidationResult.IsWarning);
            }
        }

        //updateInterval
        //combinated policy + status

        //[Fact]
        //[Trait("Category", "StringSensorValue")]
        //public void StringSensorValueErrorValidationTest()
        //{
        //    var stringSensorValue = _sensorValuesFactory.BuildStringSensorValue();
        //    stringSensorValue.StringValue = RandomGenerator.GetRandomString(TooLongStringSensorValueSize);
        //    stringSensorValue.Path = GetSensorPath(TooLongSensorValuesPathPartsCount);

        //    var result = stringSensorValue.Validate();

        //    Assert.Equal(ResultType.Error, result.Result);
        //    Assert.Equal(ValidationConstants.SensorValueIsTooLong, result.Warning);
        //    Assert.Equal(ValidationConstants.PathTooLong, result.Error);
        //}


        private static BaseSensorModel BuildSensorModel(SensorType type)
        {
            var entity = new SensorEntity()
            {
                Id = Guid.NewGuid().ToString(),
                ProductId = Guid.NewGuid().ToString(),
                DisplayName = RandomGenerator.GetRandomString(),
                Type = (byte)type
            };

            return SensorModelFactory.Build(entity);
        }

        private static string GetBadValueTypeMessage(SensorType sensorType)
        {
            var type = sensorType switch
            {
                SensorType.Boolean => typeof(BooleanValue),
                SensorType.Integer => typeof(IntegerValue),
                SensorType.Double => typeof(DoubleValue),
                SensorType.String => typeof(StringValue),
                SensorType.IntegerBar => typeof(IntegerBarValue),
                SensorType.DoubleBar => typeof(DoubleBarValue),
                SensorType.File => typeof(FileValue),
                _ => null
            };

            return string.Format(SensorValueTypeInvalid, type.Name);
        }
    }
}
