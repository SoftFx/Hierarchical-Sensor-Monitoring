namespace HSMServer.Constants
{
    internal static class ErrorConstants
    {
        public const string NameNotNull = "Name must be not null.";
        public const string NameUnique = "Name must be unique.";
        public const string ProductNameSymbols = "Product name contains forbidden characters!";
        public const string ProductNameMaxLength = "Product name max lenght is 100 characters";

        public const string SecondPasswordNotNull = "Second password must be not null.";
        public const string UsernameOrPassword = "Incorrect password or username.";

        public const string UserNotNull = "User must be not null.";

        public const string ExpirationDateNotNull = "Expiration date must be not null.";
    }
}