using HSMCommon.Constants;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests
{
    public class DataValidatorTests
    {
        [Fact]
        //This method uses data object with path that is too long (path length is 10 by default)
        public void ValidationMustReturnFalseResultForLongPath()
        {
            //Arrange
            var validator = CommonMoqs.CreateValidatorMockWithoutDatabase();
            UnitedSensorValue value = new UnitedSensorValue();
            value.Path = string.Join('/',new [] {"1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12"});
            //Act
            var result = validator.ValidateValueWithoutType(value, string.Empty, out var error);

            //Assert
            Assert.Equal(ValidationResult.Failed, result);
            Assert.Equal(ValidationConstants.PathTooLong, error);
        }

        [Fact]
        //There is currently no validation for booleans because there are no parameters
        public void ValidationResultMustBeOkForBoolean()
        {
            //Arrange
            var validator = CommonMoqs.CreateValidatorMockWithoutDatabase();
            bool value = true;
            string path = Constants.SimpleNotEmptyPath;
            string productName = Constants.SimpleNotEmptyProductName;

            //Act
            var result = validator.ValidateBoolean(value, path,productName, out var error);

            //Assert
            Assert.Equal(ValidationResult.Ok, result);
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        //There is currently no validation for int because validation method body does nothing
        public void ValidationResultMustBeOkForInt()
        {
            //Arrange
            var validator = CommonMoqs.CreateValidatorMockWithoutDatabase();
            int value = int.MaxValue;
            string path = Constants.SimpleNotEmptyPath;
            string productName = Constants.SimpleNotEmptyProductName;

            //Act
            var result = validator.ValidateInteger(value, path, productName, out var error);

            //Assert
            Assert.Equal(ValidationResult.Ok, result);
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        //There is currently no validation for double because validation method body does nothing
        public void ValidationResultMustBeOkForDouble()
        {
            //Arrange
            var validator = CommonMoqs.CreateValidatorMockWithoutDatabase();
            double value = double.Epsilon;
            string path = Constants.SimpleNotEmptyPath;
            string productName = Constants.SimpleNotEmptyProductName;

            //Act
            var result = validator.ValidateDouble(value, path, productName, out var error);

            //Assert
            Assert.Equal(ValidationResult.Ok, result);
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        //There is currently no validation for string because validation method body does nothing
        public void ValidationResultMustBeOkForString()
        {
            //Arrange
            var validator = CommonMoqs.CreateValidatorMockWithoutDatabase();
            string value = string.Empty;
            string path = Constants.SimpleNotEmptyPath;
            string productName = Constants.SimpleNotEmptyProductName;

            //Act
            var result = validator.ValidateString(value, path, productName, out var error);

            //Assert
            Assert.Equal(ValidationResult.Ok, result);
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        //There is currently no validation for IntBar because validation method body does nothing
        public void ValidationResultMustBeOkForIntBar()
        {
            //Arrange
            var validator = CommonMoqs.CreateValidatorMockWithoutDatabase();
            int max = int.MaxValue;
            int min = int.MinValue;
            int mean = 0;
            int count = 0;
            string path = Constants.SimpleNotEmptyPath;
            string productName = Constants.SimpleNotEmptyProductName;

            //Act
            var result = validator.ValidateIntBar(max, min, mean, count, path, productName, out var error);

            //Assert
            Assert.Equal(ValidationResult.Ok, result);
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        //There is currently no validation for DoubleBar because validation method body does nothing
        public void ValidationResultMustBeOkForDoubleBar()
        {
            //Arrange
            var validator = CommonMoqs.CreateValidatorMockWithoutDatabase();
            double max = double.MaxValue;
            double min = double.MinValue;
            double mean = 0.0;
            int count = 0;
            string path = Constants.SimpleNotEmptyPath;
            string productName = Constants.SimpleNotEmptyProductName;

            //Act
            var result = validator.ValidateDoubleBar(max, min, mean, count, path, productName, out var error);

            //Assert
            Assert.Equal(ValidationResult.Ok, result);
            Assert.Equal(string.Empty, error);
        }
    }
}
