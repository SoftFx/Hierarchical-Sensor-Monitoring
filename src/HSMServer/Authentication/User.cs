using System.Collections.Generic;
using System.Security.Claims;

namespace HSMServer.Authentication
{
    public class User : ClaimsPrincipal
    {
        public string UserName { get; set; }
        public string CertificateThumbprint { get; set; }
        public List<PermissionItem> UserPermissions { get; set; }
        public string CertificateFileName { get; set; }
        public string Password { get; set; }
        public UserRoleEnum Role { get; set; }
        public List<string> AvailableKeys { get; set; }

        public User(string userName, string thumbprint) : this()
        {
            UserName = userName;
            CertificateThumbprint = thumbprint;
        }
        public User()
        {
            UserPermissions = new List<PermissionItem>();
            AvailableKeys = new List<string>();
        }
    }
}
