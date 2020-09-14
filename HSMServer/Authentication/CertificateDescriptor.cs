using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Authentication
{
    public class CertificateDescriptor
    {
        private readonly string _subject;
        private readonly bool _hasPrivateKey;
        private readonly string _thumbprint;

        public CertificateDescriptor(string subject, bool hasPrivateKey, string thumbprint)
        {
            _subject = subject;
            _hasPrivateKey = hasPrivateKey;
            _thumbprint = thumbprint;
        }

        public string Subject => _subject;
        public bool HasPrivateKey => _hasPrivateKey;
        public string Thumbprint => _thumbprint;
    }
}
