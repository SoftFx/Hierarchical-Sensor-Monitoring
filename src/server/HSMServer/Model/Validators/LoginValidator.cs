using FluentValidation;
using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Model.ViewModel;

namespace HSMServer.Model.Validators
{
    public class LoginValidator : AbstractValidator<LoginViewModel>
    {
        private readonly IUserManager _userManager;
        public LoginValidator(IUserManager userManager)
        {
            _userManager = userManager;

            RuleFor(x => x.Username)
                .NotEmpty();

            RuleFor(x => x.Password)
                .NotEmpty();

            When(x => !string.IsNullOrEmpty(x.Username)
                && !string.IsNullOrEmpty(x.Password), () =>
            {
                RuleFor(x => x)
                .Must(x => _userManager.Authenticate(x.Username, x.Password) != null)
                .WithMessage(ErrorConstants.UsernameOrPassword);
            });
        }
    }
}