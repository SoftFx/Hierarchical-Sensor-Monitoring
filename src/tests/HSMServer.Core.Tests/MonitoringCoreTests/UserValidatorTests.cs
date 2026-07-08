using System.Linq;
using HSMServer.Authentication;
using HSMServer.Model.Authentication;
using HSMServer.Model.ViewModel;
using HSMServer.Model.Validators;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests
{
    public class UserValidatorTests
    {
        private const int MaxUsernameLength = BaseUserValidator<UserViewModel>.MaxUsernameLength;
        private const string ExpectedMaxLengthMessage = "Username max length is 64 characters";

        private const string ValidPassword = "12345678";


        private static Mock<IUserManager> BuildUniqueManagerMock()
        {
            var mock = new Mock<IUserManager>();
            mock.SetupGet(m => m[It.IsAny<string>()]).Returns((User)null);
            return mock;
        }

        private static string BuildUsername(int length) => new string('a', length);


        [Fact]
        public void NewUserValidator_ShouldAcceptUsernameAtMaxLength()
        {
            var validator = new NewUserValidator(BuildUniqueManagerMock().Object);
            var model = new UserViewModel { Username = BuildUsername(MaxUsernameLength), Password = ValidPassword };

            var result = validator.Validate(model);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void NewUserValidator_ShouldRejectUsernameAboveMaxLength()
        {
            var validator = new NewUserValidator(BuildUniqueManagerMock().Object);
            var model = new UserViewModel { Username = BuildUsername(MaxUsernameLength + 1), Password = ValidPassword };

            var result = validator.Validate(model);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ExpectedMaxLengthMessage);
        }

        [Fact]
        public void RegistrationValidator_ShouldAcceptUsernameAtMaxLength()
        {
            var validator = new RegistrationValidator(BuildUniqueManagerMock().Object);
            var model = new RegistrationViewModel
            {
                Username = BuildUsername(MaxUsernameLength),
                Password = ValidPassword,
                SecondPassword = ValidPassword,
            };

            var result = validator.Validate(model);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void RegistrationValidator_ShouldRejectUsernameAboveMaxLength()
        {
            var validator = new RegistrationValidator(BuildUniqueManagerMock().Object);
            var model = new RegistrationViewModel
            {
                Username = BuildUsername(MaxUsernameLength + 1),
                Password = ValidPassword,
                SecondPassword = ValidPassword,
            };

            var result = validator.Validate(model);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ExpectedMaxLengthMessage);
        }

        [Fact]
        public void UsernameMaxLengthMessage_ShouldNotContainTypo()
        {
            var validator = new RegistrationValidator(BuildUniqueManagerMock().Object);
            var model = new RegistrationViewModel
            {
                Username = BuildUsername(MaxUsernameLength + 1),
                Password = ValidPassword,
                SecondPassword = ValidPassword,
            };

            var result = validator.Validate(model);

            Assert.DoesNotContain(result.Errors, e => e.ErrorMessage.Contains("lenght"));
        }
    }
}
