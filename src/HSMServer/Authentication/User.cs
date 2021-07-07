using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace HSMServer.Authentication
{
    public class User : ClaimsPrincipal
    {
        public Guid Id { get; set; }
        public bool IsAdmin { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string CertificateThumbprint { get; set; }
        public string CertificateFileName { get; set; }
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

        public User(User user)
        {
            if (user == null) return;

            Id = user.Id;
            UserName = user.UserName;
            Password = user.Password;
            CertificateThumbprint = user.CertificateThumbprint;
            CertificateFileName = user.CertificateFileName;
            IsAdmin = user.IsAdmin;
            ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>();
            if (user.ProductsRoles != null && user.ProductsRoles.Any())
                ProductsRoles.AddRange(user.ProductsRoles);
        }
        
    }
}
