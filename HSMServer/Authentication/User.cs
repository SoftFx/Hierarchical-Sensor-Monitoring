using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Authentication
{
    public class User
    {
        public string UserName { get; set; }
        public string CertificateThumbprint { get; set; }
        public List<PermissionItem> UserPermissions { get; set; }
    }
}
