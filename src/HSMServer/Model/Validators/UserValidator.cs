﻿using FluentValidation;
using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Model.ViewModel;

namespace HSMServer.Model.Validators
{
    public class UserValidator : AbstractValidator<UserViewModel> 
    {
        private readonly IUserManager _userManager;
        public UserValidator(IUserManager userManager)
        {
            _userManager = userManager;

            RuleFor(x => x.Username)
                .NotEmpty()
                .WithMessage(ErrorConstants.UsernameNotNull);

            RuleFor(x => x.Password)
                .NotEmpty()
                .WithMessage(ErrorConstants.PasswordNotNull)
                .MinimumLength(8)
                .WithMessage(ErrorConstants.PasswordMinLength);
        }
    }
}
