namespace HSMDatabase.Exceptions
{
    public class ServerDatabaseException : System.Exception
    {
        public ServerDatabaseException()
        { }

        public ServerDatabaseException(string message) : base(message)
        { }
    }
}
