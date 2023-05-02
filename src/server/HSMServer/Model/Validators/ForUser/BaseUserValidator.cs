using FluentValidation;
using HSMServer.Authentication;
using Microsoft.AspNetCore.Identity;

namespace HSMServer.Model.Validators
{
    public abstract class BaseUserValidator<T> : AbstractValidator<T> where T : class
    {
        protected const int MaxUsernameLength = 100;
        protected const int MinPasswordLenght = 8;

        protected const string UsernameRegexTemplate = @"[a-zA-Z0-9._@+-]";

        protected const string UsernameNotNullMessage = "Username must be not null.";
        protected const string UsernameUniqueMessage = "Username must be unique.";
        
        protected const string PasswordsEqualsMessage = "Password and second password must be equals.";
        protected const string PasswordNotNullMessage = "Password must be not null.";

        protected static readonly UserOptions _options = new();

        private readonly IUserManager _manager;


        protected string UsernameMaxLengthMessage => $"Username max lenght is {MaxUsernameLength} characters";

        protected string UsernameValidSymbolsMessage => $"Username must contains only this symbols ${_options.AllowedUserNameCharacters}";

        protected string PasswordMinLengthMessage => $"Password min lenght is {MinPasswordLenght} characters.";


        protected BaseUserValidator(IUserManager manager)
        {
            _manager = manager;
        }


        protected bool IsUniqueUsername(string username) => _manager[username] == null;
    }
}