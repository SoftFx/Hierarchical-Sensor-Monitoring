using System;

namespace HSMServer.Exceptions
{
    public class UserRejectedException : Exception
    {
        public UserRejectedException()
        { }

        public UserRejectedException(string message) : base(message)
        { }
    }
}
