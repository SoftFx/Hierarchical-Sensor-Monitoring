using FluentValidation;
using HSMServer.Constants;
using HSMServer.Model.ViewModel;

namespace HSMServer.Model.Validators
{
    public class UserRightValidator : AbstractValidator<UserRightViewModel>
    {
        public UserRightValidator()
        {
            RuleFor(x => x.UserId)
                .NotEmpty()
                .WithMessage(ErrorConstants.UserNotNull);
        }
    }
}
