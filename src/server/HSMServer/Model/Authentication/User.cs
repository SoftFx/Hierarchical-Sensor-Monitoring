﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
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
    public class User : ClaimsPrincipal, IServerModel<UserEntity, UserUpdate>, INotificatable
    {
        public Guid Id { get; init; }

        public bool IsAdmin { get; set; }

        public string Name { get; init; }

        public string Password { get; init; }

        public NotificationSettings Notifications { get; init; }

        public List<(Guid, ProductRoleEnum)> ProductsRoles { get; set; }

        public TreeUserFilter TreeFilter { get; set; }


        public HistoryValuesViewModel Pagination { get; set; }


        public User(string userName) : this()
        {
            Name = userName;
        }

        public User()
        {
            Id = Guid.NewGuid();
            ProductsRoles = new List<(Guid, ProductRoleEnum)>();
            Notifications = new();
            TreeFilter = new();
        }

        public User(UserEntity entity)
        {
            if (entity == null) return;

            Id = entity.Id;
            Name = entity.UserName;
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
                : JsonSerializer.Deserialize<TreeUserFilter>(((JsonElement)entity.TreeFilter).GetRawText())?.RestoreFilterNames();
        }

        public void Update(UserUpdate update)
        {
            IsAdmin = update.IsAdmin ?? IsAdmin;
        }

        public bool IsProductAvailable(Guid productId) =>
            IsAdmin || (ProductsRoles?.Any(x => x.Item1.Equals(productId)) ?? false);

        public bool IsManager(Guid productId) =>
            IsAdmin || (ProductsRoles?.Any(x => x == (productId, ProductRoleEnum.ProductManager)) ?? false);

        public UserEntity ToEntity() =>
            new()
            {
                UserName = Name,
                Password = Password,
                Id = Id,
                IsAdmin = IsAdmin,
                ProductsRoles = ProductsRoles?.Select(r => new KeyValuePair<string, byte>(r.Item1.ToString(), (byte)r.Item2))?.ToList(),
                NotificationSettings = Notifications.ToEntity(),
                TreeFilter = TreeFilter,
            };
    }
}
