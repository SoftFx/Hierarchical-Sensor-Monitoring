using System.Collections.Generic;

namespace HSMServer.Authentication
{
    public class User
    {
        public string UserName { get; set; }
        public string CertificateThumbprint { get; set; }
        public List<PermissionItem> UserPermissions { get; set; }
        public string CertificateFileName { get; set; }

        public User(string userName, string thumbprint) : this()
        {
            UserName = userName;
            CertificateThumbprint = thumbprint;
        }
        public User()
        {
            UserPermissions = new List<PermissionItem>();
        }
    }
}
