namespace HSMServer.Constants
{
    public class ErrorConstants
    {
        public const string NameNotNull = "Name must be not null.";
        public const string NameUnique = "Name must be unique.";

        public const string PasswordNotNull = "Password must be not null.";
        public const string SecondPasswordNotNull = "Second password must be not null.";
        public const string UsernameOrPassword = "Incorrect password or username.";

        public const string UsernameNotNull = "Username must be not null.";
        public const string UsernameUnique = "Username must be unique.";
        public const string UsernameLatin = "Username must be include latin or numeric characters.";

        public const string PasswordMinLength = "Password min lenght is 8 characters.";
        public const string PasswordsEquals = "Password and second password must be equals.";

        public const string ExtraKeyNameNotNull = "Extra key name must be not null.";
        public const string ExtraKeyNameUnique = "Extra key name must be unique.";

        public const string UserNotNull = "User must be not null.";

        public const string ExpirationDateNotNull = "Expiration date must be not null.";
    }
}
