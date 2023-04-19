using FluentValidation;
using HSMServer.Authentication;
using HSMServer.Model.ViewModel;

namespace HSMServer.Model.Validators
{
    public class NewUserValidator : BaseUserValidator<UserViewModel>
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
                                    .WithMessage(PasswordNotNullMessage)
                                    .MinimumLength(MinPasswordLenght)
                                    .WithMessage(PasswordMinLengthMessage);
        }
    }
}