using FluentValidation;
using HSMServer.Constants;
using HSMServer.Core.Authentication;
using HSMServer.Model.ViewModel;
using System;

namespace HSMServer.Model.Validators
{
    public class RegistrationValidator : AbstractValidator<RegistrationViewModel>
    {
        private readonly IUserManager _userManager;
        public RegistrationValidator(IUserManager userManager)
        {
            _userManager = userManager;

            RuleFor(x => x.Username)
                .NotEmpty()
                .Must(IsUniqueUsername)
                .WithMessage(ErrorConstants.UsernameUnique)
                .Matches(@"^[0-9a-zA-Z]+$")
                .WithMessage(ErrorConstants.UsernameLatin);

            RuleFor(x => x.Password)
                .NotEmpty()
                .MinimumLength(8)
                .WithMessage(ErrorConstants.PasswordMinLength);

            RuleFor(x => x.SecondPassword)
                .NotEmpty();

            When(x => !string.IsNullOrEmpty(x.Password)
                && !string.IsNullOrEmpty(x.SecondPassword), () => 
                {
                    RuleFor(x => x)
                        .Must(x => x.Password.Equals(x.SecondPassword, StringComparison.InvariantCultureIgnoreCase))
                        .WithMessage(ErrorConstants.PasswordsEquals);
                });

        }

        private bool IsUniqueUsername(string username)
        {
            return _userManager.GetUserByUserName(username) == null;
        }


    }
}
