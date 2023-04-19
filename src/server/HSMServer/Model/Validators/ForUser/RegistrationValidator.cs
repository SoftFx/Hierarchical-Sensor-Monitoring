using FluentValidation;
using HSMServer.Authentication;
using HSMServer.Model.ViewModel;
using System;

namespace HSMServer.Model.Validators
{
    public class RegistrationValidator : BaseUserValidator<RegistrationViewModel>
    {
        public RegistrationValidator(IUserManager userManager) : base(userManager)
        {
            RuleFor(x => x.Username).NotEmpty()
                                    .WithMessage(UsernameNotNullMessage)
                                    .MaximumLength(MaxUsernameLength)
                                    .WithMessage(UsernameMaxLengthMessage)
                                    .Must(IsUniqueUsername)
                                    .WithMessage(UsernameUniqueMessage)
                                    .Matches(UsernameRegexTemplate)
                                    .WithMessage(UsernameValidSymbolsMessage);

            RuleFor(x => x.Password).NotEmpty()
                                    .WithMessage(PasswordNotNullMessage)
                                    .MinimumLength(MinPasswordLenght)
                                    .WithMessage(PasswordMinLengthMessage);

            RuleFor(x => x.SecondPassword).NotEmpty();

            When(x => !string.IsNullOrEmpty(x.Password) && !string.IsNullOrEmpty(x.SecondPassword), () =>
                {
                    RuleFor(x => x).Must(x => x.Password.Equals(x.SecondPassword, StringComparison.InvariantCultureIgnoreCase))
                                   .WithMessage(PasswordsEqualsMessage);
                });
        }
    }
}