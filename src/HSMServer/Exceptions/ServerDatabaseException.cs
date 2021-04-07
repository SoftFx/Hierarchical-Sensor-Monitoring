using System;
using LightningDB;

namespace HSMServer.Exceptions
{
    public class ServerDatabaseException : Exception
    {
        public ServerDatabaseException()
        { }

        public ServerDatabaseException(string message) : base(message)
        { }

        public ServerDatabaseException(string message, MDBResultCode code) : base($"{message}, code = {code}")
        { }

        public ServerDatabaseException(MDBResultCode code) : base($"MDBResultCode = {code}")
        { }
    }
}
