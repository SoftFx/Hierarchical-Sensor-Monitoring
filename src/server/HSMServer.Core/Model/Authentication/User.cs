using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.UserFilters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;

namespace HSMServer.Core.Model.Authentication
{
    public class User : ClaimsPrincipal, INotificatable
    {
        public Guid Id { get; set; }

        public bool IsAdmin { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public string CertificateThumbprint { get; set; }

        public string CertificateFileName { get; set; }

        public List<KeyValuePair<string, ProductRoleEnum>> ProductsRoles { get; set; }

        public UserNotificationSettings Notifications { get; internal set; }

        public TreeUserFilter TreeFilter { get; set; }


        string INotificatable.Id => Id.ToString();

        string INotificatable.Name => UserName;

        NotificationSettings INotificatable.Notifications => Notifications;

        bool INotificatable.AreNotificationsEnabled(BaseSensorModel sensor) =>
            Notifications.Telegram.MessagesAreEnabled &&
            Notifications.IsSensorEnabled(sensor.Id) &&
            !Notifications.IsSensorIgnored(sensor.Id);


        public User(string userName) : this()
        {
            UserName = userName;
        }

        public User()
        {
            Id = Guid.NewGuid();
            ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>();
            Notifications = new();
            TreeFilter = new();
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
            ProductsRoles = user.ProductsRoles != null ? new(user.ProductsRoles) : new();
            Notifications = new(user.Notifications.ToEntity());
            TreeFilter = user.TreeFilter;
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

            TreeFilter = entity.TreeFilter is null
                ? new TreeUserFilter()
                : JsonSerializer.Deserialize<TreeUserFilter>(((JsonElement)entity.TreeFilter).GetRawText());
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
            ProductsRoles = user.ProductsRoles != null ? new(user.ProductsRoles) : new();
            Notifications = new(user.Notifications.ToEntity());
            TreeFilter = user.TreeFilter;
        }

        public User Copy()
        {
            var copy = this.MemberwiseClone() as User;
            copy.ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>(ProductsRoles);
            copy.Notifications = new(Notifications.ToEntity());
            copy.TreeFilter = TreeFilter;
            return copy;
        }

        public bool IsProductAvailable(string productId) =>
            IsAdmin || (ProductsRoles?.Any(x => x.Key.Equals(productId)) ?? false);

        public List<string> GetManagerProducts() =>
            ProductsRoles.Where(r => r.Value == ProductRoleEnum.ProductManager).Select(r => r.Key).ToList();
    }
}
