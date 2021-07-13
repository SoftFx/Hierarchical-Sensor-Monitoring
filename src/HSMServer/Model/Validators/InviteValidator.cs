using FluentValidation;
using HSMServer.Constants;
using HSMServer.Model.ViewModel;

namespace HSMServer.Model.Validators
{
    public class InviteValidator : AbstractValidator<InviteViewModel>
    {
        public InviteValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress();

            //RuleFor(x => x.ExpirationDate)
                //.NotNull()
                //.WithMessage(ErrorConstants.ExpirationDateNotNull);
        }
    }
}
