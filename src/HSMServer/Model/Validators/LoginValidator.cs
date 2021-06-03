using FluentValidation;
using HSMServer.Authentication;
using HSMServer.Constants;

namespace HSMServer.Model.Validators
{
    public class LoginValidator : AbstractValidator<LoginViewModel>
    {
        private readonly IUserManager _userManager;
        public LoginValidator(IUserManager userManager)
        {
            _userManager = userManager;

            RuleFor(x => x.Login)
                .NotNull()
                .WithMessage(ErrorConstants.LoginNotNull);

            RuleFor(x => x.Password)
                .NotNull()
                .WithMessage(ErrorConstants.PasswordNotNull);

            When(x => x.Login != null && x.Password != null, () =>
            {
                RuleFor(x => x)
                .Must(x => _userManager.Authenticate(x.Login, x.Password) != null)
                .WithMessage(ErrorConstants.LoginOrPassword);
            });
                      
        }
    }
}
