using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace HSMServer.Core.Model.Authentication
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


        public NotificationSettings Notifications { get; internal set; }
        public Filter Filter { get; set; }


        public User(string userName) : this()
        {
            UserName = userName;
        }

        public User()
        {
            Id = Guid.NewGuid();
            ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>();
            Notifications = new();
            Filter = new();
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

            Notifications = new(user.Notifications.ToEntity());
            Filter = new(user.Filter.ToEntity());
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

            Notifications = new(entity.NotificationSettings);
            Filter = new(entity.Filter);
        }

        /// <summary>
        /// Update works as HTTP PUT: all the fields will be updated
        /// </summary>
        /// <param name="user"></param>
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

            Notifications = new(user.Notifications.ToEntity());
            Filter = new(user.Filter.ToEntity());
        }

        public User Copy()
        {
            var copy = this.MemberwiseClone() as User;
            copy.ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>(ProductsRoles);
            copy.Notifications = new(Notifications.ToEntity());
            copy.Filter = new(Filter.ToEntity());
            return copy;
        }
    }
}
