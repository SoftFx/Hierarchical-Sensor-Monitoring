using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace HSMServer.Authentication
{
    public class User : ClaimsPrincipal
    {
        public Guid Id { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string CertificateThumbprint { get; set; }
        public string CertificateFileName { get; set; }
        public UserRoleEnum Role { get; set; }
        public List<KeyValuePair<string, ProductRoleEnum>> ProductsRoles { get; set; }

        public User()
        {
            ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>();
        }

        public User(string userName, string thumbprint) : this()
        {
            UserName = userName;
            CertificateThumbprint = thumbprint;
        }
        
    }
}
