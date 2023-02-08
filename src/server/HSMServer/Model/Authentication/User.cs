using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.UserFilters;
using HSMServer.Model.Authentication.History;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;

namespace HSMServer.Model.Authentication
{
    public class User : ClaimsPrincipal, INotificatable
    {
        public Guid Id { get; set; }

        public bool IsAdmin { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public List<(Guid, ProductRoleEnum)> ProductsRoles { get; set; }

        public NotificationSettings Notifications { get; set; }

        public TreeUserFilter TreeFilter { get; set; }


        public HistoryValuesViewModel Pagination { get; set; }


        string INotificatable.Name => UserName;

        NotificationSettings INotificatable.Notifications => Notifications;
        

        public User(string userName) : this()
        {
            UserName = userName;
        }

        public User()
        {
            Id = Guid.NewGuid();
            ProductsRoles = new List<(Guid, ProductRoleEnum)>();
            Notifications = new();
            TreeFilter = new();
        }


        public User(User user)
        {
            if (user == null) return;

            Id = user.Id;
            UserName = user.UserName;
            Password = user.Password;
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
            Password = entity.Password;
            IsAdmin = entity.IsAdmin;

            ProductsRoles = new List<(Guid, ProductRoleEnum)>();
            if (entity.ProductsRoles != null && entity.ProductsRoles.Any())
            {
                ProductsRoles.AddRange(entity.ProductsRoles.Select(
                    r => (Guid.Parse(r.Key), (ProductRoleEnum)r.Value)));
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
            Password = user.Password;
            IsAdmin = user.IsAdmin;
            ProductsRoles = user.ProductsRoles != null ? new(user.ProductsRoles) : new();
            Notifications = new(user.Notifications.ToEntity());
            TreeFilter = user.TreeFilter;
        }

        public User Copy()
        {
            var copy = this.MemberwiseClone() as User;
            copy.ProductsRoles = new List<(Guid, ProductRoleEnum)>(ProductsRoles);
            copy.Notifications = new(Notifications.ToEntity());
            copy.TreeFilter = TreeFilter;
            return copy;
        }

        public bool IsProductAvailable(Guid productId) =>
            IsAdmin || (ProductsRoles?.Any(x => x.Item1.Equals(productId)) ?? false);

        public List<Guid> GetManagerProducts() =>
            ProductsRoles.Where(r => r.Item2 == ProductRoleEnum.ProductManager).Select(r => r.Item1).ToList();

        internal UserEntity ToEntity() =>
            new()
            {
                UserName = UserName,
                Password = Password,
                Id = Id,
                IsAdmin = IsAdmin,
                ProductsRoles = ProductsRoles?.Select(r => new KeyValuePair<string, byte>(r.Item1.ToString(), (byte)r.Item2))?.ToList(),
                NotificationSettings = Notifications.ToEntity(),
                TreeFilter = TreeFilter,
            };
    }
}
