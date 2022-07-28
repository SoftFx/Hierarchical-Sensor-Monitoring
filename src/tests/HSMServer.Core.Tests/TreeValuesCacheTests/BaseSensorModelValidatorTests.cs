using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
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

        private readonly ITreeValuesCache _valuesCache;
        private ProductModel _testProduct;
        //private const int TooLongSensorValuesPathPartsCount = 11;
        //private const int MaxSensorValuesPathPartsCount = 10;

        //private const int TooLongStringSensorValueSize = 151;
        //private const int MaxStringSensorValueSize = 150;


        public BaseSensorModelValidatorTests(ValidationFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _valuesCache = new TreeValuesCache(_databaseCoreManager.DatabaseCore, _userManager, _updatesQueue);

            _testProduct = _valuesCache.AddProduct(TestProductsManager.ProductName);
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
            var sensor = SensorModelFactory.Build(BuildEntity(type));

            Assert.False(sensor.TryAddValue(null, out _));
        }

        [Fact]
        [Trait("Category", "StringSensorModelWarning")]
        public void StringSensorModelWarningValidationTest()
        {
            var stringModel = SensorModelFactory.Build(BuildEntity(SensorType.String));
            stringModel.AddPolicy(new StringValueLengthPolicy());

            var stringBase = new StringValue 
            { 
                Value = RandomGenerator.GetRandomString(DefaultMaxStringLength + 1) 
            };

            Assert.True(stringModel.TryAddValue(stringBase, out _));
            Assert.True(stringModel.ValidationResult.IsWarning);
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
            var sensor = SensorModelFactory.Build(BuildEntity(sensorType));
            var errorMessage = GetBadValueTypeMessage(sensorType);

            foreach (var invalidType in Enum.GetValues<SensorType>())
            {
                if (invalidType == sensorType)
                    break;

                Assert.False(sensor.TryAddValue(SensorValuesFactory.BuildSensorValue(invalidType), out _));
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
                var sensor = SensorModelFactory.Build(BuildEntity(sensorType));
                var baseValue = SensorValuesFactory.BuildSensorValue(sensorType) with { Status = status };

                var errorMessage = status == SensorStatus.Unknown ?
                    UnexpectedBehaviorMessage
                    : string.Format(SensorValueStatusInvalid, status);

                Assert.True(sensor.TryAddValue(baseValue, out _));
                Assert.Equal(baseValue.Status, sensor.ValidationResult.Result);
                Assert.Equal(errorMessage, sensor.ValidationResult.Message);
            }
        }

        //updateInterval

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

        #region United

        //[Theory]
        //[InlineData(SensorType.BooleanSensor)]
        //[InlineData(SensorType.IntSensor)]
        //[InlineData(SensorType.DoubleSensor)]
        //[InlineData(SensorType.StringSensor)]
        //[InlineData(SensorType.IntegerBarSensor)]
        //[InlineData(SensorType.DoubleBarSensor)]
        //[Trait("Category", "UnitedSensorValue")]
        //public void UnitedSensorValueWarningValidationTest(SensorType type)
        //{
        //    var unitedSensorValue = _sensorValuesFactory.BuildUnitedSensorValue(type);
        //    unitedSensorValue.Data = RandomGenerator.GetRandomString(TooLongUnitedSensorValueDataSize);

        //    var result = unitedSensorValue.Validate();

        //    Assert.Equal(ResultType.Warning, result.ResultType);
        //    Assert.Equal(ValidationConstants.SensorValueIsTooLong, result.Warning);
        //    Assert.True(string.IsNullOrEmpty(result.Error));
        //}

        //[Theory]
        //[InlineData(SensorType.FileSensor)]
        //[InlineData(SensorType.FileSensorBytes)]
        //[Trait("Category", "UnitedSensorValue")]
        //public void UnitedSensorValueErrorValidationTest(SensorType type)
        //{
        //    var unitedSensorValue = _sensorValuesFactory.BuildRandomUnitedSensorValue();
        //    unitedSensorValue.Type = type;

        //    var result = unitedSensorValue.Validate();

        //    Assert.Equal(ResultType.Error, result.ResultType);
        //    Assert.Equal(ValidationConstants.FailedToParseType, result.Error);
        //    Assert.True(string.IsNullOrEmpty(result.Warning));
        //}

        //[Fact]
        //[Trait("Category", "UnitedSensorValue")]
        //public void UnitedSensorValueAllErrorsValidationTest()
        //{
        //    var unitedSensorValue = _sensorValuesFactory.BuildUnitedSensorValue(SensorType.FileSensorBytes);
        //    unitedSensorValue.Data = RandomGenerator.GetRandomString(TooLongUnitedSensorValueDataSize);
        //    unitedSensorValue.Path = GetSensorPath(TooLongSensorValuesPathPartsCount);

        //    var result = unitedSensorValue.Validate();

        //    var errors = new List<string>(2)
        //    {
        //        ValidationConstants.FailedToParseType,
        //        ValidationConstants.PathTooLong
        //    };

        //    Assert.Equal(ResultType.Error, result.ResultType);
        //    Assert.Equal(ValidationConstants.SensorValueIsTooLong, result.Warning);
        //    Assert.Equal(string.Join(Environment.NewLine, errors), result.Error);
        //}

        //[Theory]
        //[InlineData(SensorType.BooleanSensor)]
        //[InlineData(SensorType.IntSensor)]
        //[InlineData(SensorType.DoubleSensor)]
        //[InlineData(SensorType.StringSensor)]
        //[InlineData(SensorType.IntegerBarSensor)]
        //[InlineData(SensorType.DoubleBarSensor)]
        //[Trait("Category", "UnitedSensorValue")]
        //public void UnitedSensorValueValidationTest(SensorType type)
        //{
        //    var unitedSensorValue = _sensorValuesFactory.BuildUnitedSensorValue(type);
        //    unitedSensorValue.Data = RandomGenerator.GetRandomString(MaxUnitedSensorValueDataSize);

        //    TestCorrectData(unitedSensorValue.Validate());
        //}


        //private UnitedSensorValue BuildUnitedSensorValue(int pathParts)
        //{
        //    var sensorValue = _sensorValuesFactory.BuildRandomUnitedSensorValue();
        //    sensorValue.Path = GetSensorPath(pathParts);

        //    return sensorValue;
        //}

        #endregion


        private SensorEntity BuildEntity(SensorType type) => new()
        {
            Id = Guid.NewGuid().ToString(),
            ProductId = _testProduct.Id,
            DisplayName = RandomGenerator.GetRandomString(),
            Type = (byte)type
        };

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
