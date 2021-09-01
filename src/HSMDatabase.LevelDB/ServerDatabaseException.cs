using System;

namespace HSMDatabase.LevelDB
{
    public class ServerDatabaseException : System.Exception
    {
        public ServerDatabaseException()
        { }

        public ServerDatabaseException(string message) : base(message)
        { }

        public ServerDatabaseException(string message, Exception ex) : base(message, ex)
        { }
    }
}
