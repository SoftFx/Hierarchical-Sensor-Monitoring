using HSMCommon.Constants;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using HSMServer.Core.SensorsDataValidation;
using HSMServer.Core.Tests.Infrastructure;
using System.Linq;
using Xunit;

namespace HSMServer.Core.DataValidatorTests
{
    public class DataValidatorTests
    {
        private const string Path = Constants.SimpleNotEmptyPath;
        private const string ProductName = Constants.SimpleNotEmptyProductName;

        private readonly ISensorsDataValidator _validator;


        public DataValidatorTests()
        {
            _validator = CommonMoqs.CreateValidatorMockWithoutDatabase();
        }


        [Fact]
        public void LongPathValidationTest()
        {
            var unitedValue = BuildSensorValue(11);

            var result = _validator.ValidateValueWithoutType(unitedValue, out var error);

            Assert.Equal(ValidationResult.Failed, result);
            Assert.Equal(ValidationConstants.PathTooLong, error);
        }

        [Fact]
        public void CorrectPathValidationTest()
        {
            var unitedValue = BuildSensorValue(10);

            var result = _validator.ValidateValueWithoutType(unitedValue, out var error);

            TestCorrectData(result, error);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BoolValidationtTest(bool value)
        {
            var result = _validator.ValidateBoolean(value, Path, ProductName, out var error);

            TestCorrectData(result, error);
        }

        [Fact]
        public void IntValidationTest()
        {
            int value = RandomValuesGenerator.GetRandomInt();

            var result = _validator.ValidateInteger(value, Path, ProductName, out var error);

            TestCorrectData(result, error);
        }

        [Fact]
        //There is currently no validation for double because validation method body does nothing
        public void DoubleValidationTest()
        {
            double value = RandomValuesGenerator.GetRandomDouble();

            var result = _validator.ValidateDouble(value, Path, ProductName, out var error);

            TestCorrectData(result, error);
        }

        [Fact]
        public void StringValidationTest()
        {
            string value = RandomValuesGenerator.GetRandomString();

            var result = _validator.ValidateString(value, Path, ProductName, out var error);

            TestCorrectData(result, error);
        }

        [Fact]
        public void IntBarValidationTest()
        {
            int max = RandomValuesGenerator.GetRandomInt();
            int min = RandomValuesGenerator.GetRandomInt();
            int mean = RandomValuesGenerator.GetRandomInt();
            int count = RandomValuesGenerator.GetRandomInt(positive: true);

            var result = _validator.ValidateIntBar(max, min, mean, count, Path, ProductName, out var error);

            TestCorrectData(result, error);
        }

        [Fact]
        public void DoubleBarValidationTest()
        {
            double max = RandomValuesGenerator.GetRandomDouble();
            double min = RandomValuesGenerator.GetRandomDouble();
            double mean = RandomValuesGenerator.GetRandomDouble();
            int count = RandomValuesGenerator.GetRandomInt(positive: true);

            var result = _validator.ValidateDoubleBar(max, min, mean, count, Path, ProductName, out var error);

            TestCorrectData(result, error);
        }


        private static void TestCorrectData(ValidationResult result, string error)
        {
            Assert.Equal(ValidationResult.Ok, result);
            Assert.Equal(string.Empty, error);
        }

        private static UnitedSensorValue BuildSensorValue(int pathParts) =>
            new()
            {
                Path = string.Join('/', Enumerable.Range(0, pathParts)),
            };
    }
}
