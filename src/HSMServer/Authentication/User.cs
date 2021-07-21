using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using HSMDatabase.Entity;

namespace HSMServer.Authentication
{
    public class User : ClaimsPrincipal
    {
        public Guid Id { get; }
        public bool IsAdmin { get; set; }
        public string UserName { get; }
        public string Password { get; set; }
        public string CertificateThumbprint { get; set; }
        public string CertificateFileName { get; set; }
        public List<KeyValuePair<string, ProductRoleEnum>> ProductsRoles { get; set; }
        public User(string userName) : this()
        {
            UserName = userName;
        }
        public User()
        {
            Id = Guid.NewGuid();
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

        public User(UserEntity entity)
        {
            if (entity == null) return;

            Id = entity.Id;
            UserName = entity.UserName;
            CertificateFileName = entity.CertificateFileName;
            CertificateThumbprint = entity.CertificateThumbprint;
            Password = entity.Password;
            IsAdmin = entity.IsAdmin;
            ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>();
            if (entity.ProductsRoles != null && entity.ProductsRoles.Any())
            {
                ProductsRoles.AddRange(entity.ProductsRoles.Select(
                    r => new KeyValuePair<string, ProductRoleEnum>(r.Key, (ProductRoleEnum)r.Value)));
            }
        }

        public void Update(User user)
        {
            //CertificateFileName = user.CertificateFileName;
            //CertificateThumbprint = user.CertificateThumbprint;
            Password = user.Password;
            IsAdmin = user.IsAdmin;
            ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>();
            if (user.ProductsRoles != null && user.ProductsRoles.Any())
            {
                ProductsRoles.AddRange(user.ProductsRoles);
            }
        }
    }
}
