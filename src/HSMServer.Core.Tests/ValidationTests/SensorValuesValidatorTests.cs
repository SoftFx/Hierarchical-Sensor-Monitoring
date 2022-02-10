using HSMCommon.Constants;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.SensorsDataValidation;
using HSMServer.Core.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.ValidationTests
{
    public class SensorValuesValidatorTests : IClassFixture<ValidationFixture>
    {
        private const int TooLongSensorValuesPathPartsCount = 11;
        private const int MaxSensorValuesPathPartsCount = 10;

        private const int TooLongStringSensorValueSize = 151;
        private const int MaxStringSensorValueSize = 150;

        private const int TooLongUnitedSensorValueDataSize = 1025;
        private const int MaxUnitedSensorValueDataSize = 1024;


        private readonly SensorValuesFactory _sensorValuesFactory = new(TestProductsManager.TestProduct.Key);


        [Fact]
        [Trait("Category", "SensorValueBase")]
        public void LongPathValidationTest()
        {
            var unitedValue = BuildUnitedSensorValue(TooLongSensorValuesPathPartsCount);

            var result = unitedValue.Validate();

            Assert.Equal(ResultType.Error, result.ResultType);
            Assert.Equal(ValidationConstants.PathTooLong, result.Error);
            Assert.True(string.IsNullOrEmpty(result.Warning));
        }

        [Fact]
        [Trait("Category", "SensorValueBase")]
        public void NullSensorValueValidationTest()
        {
            const SensorValueBase value = null;

            var result = value.Validate();

            Assert.Equal(ResultType.Error, result.ResultType);
            Assert.Equal(ValidationConstants.ObjectIsNull, result.Error);
            Assert.True(string.IsNullOrEmpty(result.Warning));
        }

        [Fact]
        [Trait("Category", "SensorValueBase")]
        public void SensorValueValidationTest()
        {
            var unitedValue = BuildUnitedSensorValue(MaxSensorValuesPathPartsCount);

            TestCorrectData(unitedValue.Validate());
        }

        [Fact]
        [Trait("Category", "StringSensorValue")]
        public void StringSensorValueWarningValidationTest()
        {
            var stringSensorValue = _sensorValuesFactory.BuildStringSensorValue();
            stringSensorValue.StringValue = RandomGenerator.GetRandomString(TooLongStringSensorValueSize);

            var result = stringSensorValue.Validate();

            Assert.Equal(ResultType.Warning, result.ResultType);
            Assert.Equal(ValidationConstants.SensorValueIsTooLong, result.Warning);
            Assert.True(string.IsNullOrEmpty(result.Error));
        }

        [Fact]
        [Trait("Category", "StringSensorValue")]
        public void StringSensorValueErrorValidationTest()
        {
            var stringSensorValue = _sensorValuesFactory.BuildStringSensorValue();
            stringSensorValue.StringValue = RandomGenerator.GetRandomString(TooLongStringSensorValueSize);
            stringSensorValue.Path = GetSensorPath(TooLongSensorValuesPathPartsCount);

            var result = stringSensorValue.Validate();

            Assert.Equal(ResultType.Error, result.ResultType);
            Assert.Equal(ValidationConstants.SensorValueIsTooLong, result.Warning);
            Assert.Equal(ValidationConstants.PathTooLong, result.Error);
        }

        [Fact]
        [Trait("Category", "StringSensorValue")]
        public void StringSensorValueValidationTest()
        {
            var stringSensorValue = _sensorValuesFactory.BuildStringSensorValue();
            stringSensorValue.StringValue = RandomGenerator.GetRandomString(MaxStringSensorValueSize);

            TestCorrectData(stringSensorValue.Validate());
        }

        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [Trait("Category", "UnitedSensorValue")]
        public void UnitedSensorValueWarningValidationTest(SensorType type)
        {
            var unitedSensorValue = _sensorValuesFactory.BuildUnitedSensorValue(type);
            unitedSensorValue.Data = RandomGenerator.GetRandomString(TooLongUnitedSensorValueDataSize);

            var result = unitedSensorValue.Validate();

            Assert.Equal(ResultType.Warning, result.ResultType);
            Assert.Equal(ValidationConstants.SensorValueIsTooLong, result.Warning);
            Assert.True(string.IsNullOrEmpty(result.Error));
        }

        [Theory]
        [InlineData(SensorType.FileSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [Trait("Category", "UnitedSensorValue")]
        public void UnitedSensorValueErrorValidationTest(SensorType type)
        {
            var unitedSensorValue = _sensorValuesFactory.BuildRandomUnitedSensorValue();
            unitedSensorValue.Type = type;

            var result = unitedSensorValue.Validate();

            Assert.Equal(ResultType.Error, result.ResultType);
            Assert.Equal(ValidationConstants.FailedToParseType, result.Error);
            Assert.True(string.IsNullOrEmpty(result.Warning));
        }

        [Fact]
        [Trait("Category", "UnitedSensorValue")]
        public void UnitedSensorValueAllErrorsValidationTest()
        {
            var unitedSensorValue = _sensorValuesFactory.BuildUnitedSensorValue(SensorType.FileSensor);
            unitedSensorValue.Data = RandomGenerator.GetRandomString(TooLongUnitedSensorValueDataSize);
            unitedSensorValue.Path = GetSensorPath(TooLongSensorValuesPathPartsCount);

            var result = unitedSensorValue.Validate();

            var errors = new List<string>(2)
            {
                ValidationConstants.FailedToParseType,
                ValidationConstants.PathTooLong
            };

            Assert.Equal(ResultType.Error, result.ResultType);
            Assert.Equal(ValidationConstants.SensorValueIsTooLong, result.Warning);
            Assert.Equal(string.Join(Environment.NewLine, errors), result.Error);
        }

        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [Trait("Category", "UnitedSensorValue")]
        public void UnitedSensorValueValidationTest(SensorType type)
        {
            var unitedSensorValue = _sensorValuesFactory.BuildUnitedSensorValue(type);
            unitedSensorValue.Data = RandomGenerator.GetRandomString(MaxUnitedSensorValueDataSize);

            TestCorrectData(unitedSensorValue.Validate());
        }


        private UnitedSensorValue BuildUnitedSensorValue(int pathParts)
        {
            var sensorValue = _sensorValuesFactory.BuildRandomUnitedSensorValue();
            sensorValue.Path = GetSensorPath(pathParts);

            return sensorValue;
        }


        private static void TestCorrectData(ValidationResult result)
        {
            Assert.Equal(ResultType.Ok, result.ResultType);

            Assert.True(string.IsNullOrEmpty(result.Warning));
            Assert.True(string.IsNullOrEmpty(result.Error));
        }

        private static string GetSensorPath(int pathParts) =>
             string.Join('/', Enumerable.Range(0, pathParts));
    }
}
