using FluentValidation;
using HSMServer.Authentication;
using HSMServer.Model.ViewModel;

namespace HSMServer.Model.Validators
{
    public class EditUserValidator : BaseUserValidator<UserViewModel>
    {
        public EditUserValidator(IUserManager userManager = null) : base(userManager)
        {
            RuleFor(x => x.Password).Must(p => string.IsNullOrEmpty(p) || p.Length >= MinPasswordLenght)
                                    .WithMessage(PasswordMinLengthMessage);
        }
    }
}
