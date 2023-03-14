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

            RuleFor(x => x.Username.Trim())
                .NotEmpty()
                .WithMessage(ErrorConstants.UsernameNotNull)
                .MaximumLength(100)
                .WithMessage(ErrorConstants.UsernameMaxLength)
                .Must(IsUniqueUsername)
                .WithMessage(ErrorConstants.UsernameUnique)
                .Matches(@"^[0-9a-zA-Z]+$")
                .WithMessage(ErrorConstants.UsernameLatin);

            RuleFor(x => x.Password)
                .NotEmpty()
                .WithMessage(ErrorConstants.PasswordNotNull)
                .MinimumLength(8)
                .WithMessage(ErrorConstants.PasswordMinLength);
        }

        private bool IsUniqueUsername(string username) => _userManager[username] == null;
    }
}
