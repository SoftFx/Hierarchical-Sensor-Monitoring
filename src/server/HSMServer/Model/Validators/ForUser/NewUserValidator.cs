using FluentValidation;
using HSMServer.Authentication;

namespace HSMServer.Model.Validators
{
    public class NewUserValidator : EditUserValidator
    {
        public NewUserValidator(IUserManager userManager) : base(userManager)
        {
            RuleFor(x => x.Username.Trim()).NotEmpty()
                                           .WithMessage(UsernameNotNullMessage)
                                           .MaximumLength(MaxUsernameLength)
                                           .WithMessage(UsernameMaxLengthMessage)
                                           .Must(IsUniqueUsername)
                                           .WithMessage(UsernameUniqueMessage)
                                           .Matches(UsernameRegexTemplate)
                                           .WithMessage(UsernameValidSymbolsMessage);

            RuleFor(x => x.Password).NotEmpty()
                                    .WithMessage(PasswordNotNullMessage);
        }
    }
}