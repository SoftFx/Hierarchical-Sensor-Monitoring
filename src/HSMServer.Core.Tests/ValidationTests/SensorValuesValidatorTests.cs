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
        private readonly SensorValuesFactory _sensorValuesFactory = new(DatabaseAdapterManager.ProductName);


        [Fact]
        [Trait("Category", "SensorValueBase")]
        public void LongPathValidationTest()
        {
            var unitedValue = BuildUnitedSensorValue(11);

            var result = unitedValue.Validate();

            Assert.Equal(ResultType.Failed, result.ResultType);
            Assert.Equal(ValidationConstants.PathTooLong, result.Error);
        }

        [Fact]
        [Trait("Category", "SensorValueBase")]
        public void NullSensorValueValidationTest()
        {
            const SensorValueBase value = null;

            var result = value.Validate();

            Assert.Equal(ResultType.Failed, result.ResultType);
            Assert.Equal(ValidationConstants.ObjectIsNull, result.Error);
        }

        [Fact]
        [Trait("Category", "SensorValueBase")]
        public void SensorValueValidationTest()
        {
            var unitedValue = BuildUnitedSensorValue(10);

            TestCorrectData(unitedValue.Validate());
        }

        [Fact]
        [Trait("Category", "StringSensorValue")]
        public void StringSensorValueWarningValidationTest()
        {
            var stringSensorValue = _sensorValuesFactory.BuildStringSensorValue();
            stringSensorValue.StringValue = RandomValuesGenerator.GetRandomString(151);

            var result = stringSensorValue.Validate();

            Assert.Equal(ResultType.Warning, result.ResultType);
            Assert.Equal(ValidationConstants.SensorValueIsTooLong, result.Error);
        }

        [Fact]
        [Trait("Category", "StringSensorValue")]
        public void StringSensorValueErrorValidationTest()
        {
            var stringSensorValue = _sensorValuesFactory.BuildStringSensorValue();
            stringSensorValue.StringValue = RandomValuesGenerator.GetRandomString(151);
            stringSensorValue.Path = GetSensorPath(11);

            var result = stringSensorValue.Validate();

            var errors = new List<string>(2)
            {
                ValidationConstants.PathTooLong,
                ValidationConstants.SensorValueIsTooLong
            };

            Assert.Equal(ResultType.Failed, result.ResultType);
            Assert.Equal(string.Join(Environment.NewLine, errors), result.Error);
        }

        [Fact]
        [Trait("Category", "StringSensorValue")]
        public void StringSensorValueValidationTest()
        {
            var stringSensorValue = _sensorValuesFactory.BuildStringSensorValue();
            stringSensorValue.StringValue = RandomValuesGenerator.GetRandomString(150);

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
            unitedSensorValue.Data = RandomValuesGenerator.GetRandomString(1025);

            var result = unitedSensorValue.Validate();

            Assert.Equal(ResultType.Warning, result.ResultType);
            Assert.Equal(ValidationConstants.SensorValueIsTooLong, result.Error);
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

            Assert.Equal(ResultType.Failed, result.ResultType);
            Assert.Equal(ValidationConstants.FailedToParseType, result.Error);
        }

        [Fact]
        [Trait("Category", "UnitedSensorValue")]
        public void UnitedSensorValueAllErrorsValidationTest()
        {
            var unitedSensorValue = _sensorValuesFactory.BuildUnitedSensorValue(SensorType.FileSensor);
            unitedSensorValue.Data = RandomValuesGenerator.GetRandomString(1025);
            unitedSensorValue.Path = GetSensorPath(11);

            var result = unitedSensorValue.Validate();

            var errors = new List<string>(3)
            {
                ValidationConstants.FailedToParseType,
                ValidationConstants.SensorValueIsTooLong,
                ValidationConstants.PathTooLong
            };

            Assert.Equal(ResultType.Failed, result.ResultType);
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
            unitedSensorValue.Data = RandomValuesGenerator.GetRandomString(1024);

            TestCorrectData(unitedSensorValue.Validate());
        }


        private UnitedSensorValue BuildUnitedSensorValue(int pathParts)
        {
            var sensorValue = _sensorValuesFactory.BuildRandomUnitedSensorValue();
            sensorValue.Path = GetSensorPath(pathParts);

            return sensorValue;
        }


        private static void TestCorrectData<T>(ValidationResult<T> result)
        {
            Assert.Equal(ResultType.Ok, result.ResultType);
            Assert.Equal(new List<string>(), result.Errors);
        }

        private static string GetSensorPath(int pathParts) =>
             string.Join('/', Enumerable.Range(0, pathParts));
    }
}
