namespace HSMServer.Constants
{
    public class ErrorConstants
    {
        public const string NameNotNull = "Name must be not null.";
        public const string NameUnique = "Name must be unique.";

        public const string LoginNotNull = "Login must be not null.";
        public const string PasswordNotNull = "Password must be not null.";
        public const string LoginOrPassword = "Incorrect password or username.";

        public const string UsernameNotNull = "Username must be not null.";
        public const string PasswordMinLength = "Password min lenght is 8 characters.";

        public const string ExtraKeyNameNotNull = "Extra key name must be not null.";
        public const string ExtraKeyNameUnique = "Extra key name must be unique.";
    }
}
