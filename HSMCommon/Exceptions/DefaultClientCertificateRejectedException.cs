using System;

namespace HSMCommon.Exceptions
{
    public class DefaultClientCertificateRejectedException : Exception
    {
        public DefaultClientCertificateRejectedException()
        { }

        public DefaultClientCertificateRejectedException(string message) : base(message)
        { }
    }
}
