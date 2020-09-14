using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using MAMSClient.Configuration;

namespace HSMClient.Configuration
{
    public class ConnectionInfo
    {
        public string Address { get; set; }
        public string Port { get; set; }
        public UserInfo UserInfo { get; set; }
        public X509Certificate2 ClientCertificate { get; set; }
    }
}
