using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Model.History;
using HSMServer.Notification.Settings;
using HSMServer.UserFilters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModels;

namespace HSMServer.Model.Authentication
{
    public class User : ClaimsPrincipal, IServerModel<UserEntity, UserUpdate>, INotificatable
    {
        public Guid Id { get; init; }

        public bool IsAdmin { get; set; }

        public string Name { get; init; }

        public string Password { get; init; }

        public ClientNotifications Notifications { get; init; }

        public List<(Guid, ProductRoleEnum)> ProductsRoles { get; set; } = new();

        public Dictionary<Guid, ProductRoleEnum> FoldersRoles { get; } = new();

        public TreeUserFilter TreeFilter { get; set; }

        public VisibleTreeViewModel Tree { get; }

        public SelectedSensorHistoryViewModel History { get; } = new();


        public User(string userName) : this()
        {
            Name = userName;
        }

        public User()
        {
            Id = Guid.NewGuid();
            Notifications = new();
            TreeFilter = new();
            Tree = new VisibleTreeViewModel(this);
        }

        public User(UserEntity entity)
        {
            if (entity == null)
                return;

            Id = entity.Id;
            Name = entity.UserName;
            Password = entity.Password;
            IsAdmin = entity.IsAdmin;
            Notifications = new(entity.NotificationSettings);

            if (entity.ProductsRoles != null)
                ProductsRoles.AddRange(entity.ProductsRoles.Select(r => (r.Key.ToGuid(), (ProductRoleEnum)r.Value)));

            foreach (var (folderId, role) in entity.FolderRoles)
                FoldersRoles.Add(folderId.ToGuid(), (ProductRoleEnum)role);

            TreeFilter = entity.TreeFilter is null
                ? new TreeUserFilter()
                : JsonSerializer.Deserialize<TreeUserFilter>(((JsonElement)entity.TreeFilter).GetRawText())?.RestoreFilterNames();

            Tree = new VisibleTreeViewModel(this);
        }


        public void Update(UserUpdate update)
        {
            IsAdmin = update.IsAdmin ?? IsAdmin;
        }

        public UserEntity ToEntity() =>
            new()
            {
                UserName = Name,
                Password = Password,
                Id = Id,
                IsAdmin = IsAdmin,
                FolderRoles = FoldersRoles.ToDictionary(f => f.Key.ToString(), f => (byte)f.Value),
                ProductsRoles = ProductsRoles.Select(r => new KeyValuePair<string, byte>(r.Item1.ToString(), (byte)r.Item2)).ToList(),
                NotificationSettings = Notifications.ToEntity(),
                TreeFilter = TreeFilter,
            };


        internal bool IsManager(Guid productId) =>
            IsAdmin || ProductsRoles.Contains((productId, ProductRoleEnum.ProductManager));

        internal bool IsProductAvailable(Guid productId) => IsAdmin || IsUserProduct(productId);

        internal bool IsFolderAvailable(Guid folderId) => IsAdmin || FoldersRoles.ContainsKey(folderId);

        internal bool IsFolderManager(Guid folderId) => IsAdmin ||
            (FoldersRoles.TryGetValue(folderId, out var role) && role == ProductRoleEnum.ProductManager);

        internal bool IsUserProduct(Guid productId) => ProductsRoles.Any(x => x.Item1 == productId);
    }
}
