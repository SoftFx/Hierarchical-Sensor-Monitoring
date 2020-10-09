using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Exceptions
{
    public class ServerDatabaseException : Exception
    {
        public ServerDatabaseException()
        { }

        public ServerDatabaseException(string message) : base(message)
        { }
    }
}
