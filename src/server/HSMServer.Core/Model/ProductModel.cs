﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace HSMServer.Core.Model
{
    [Flags]
    public enum ProductState : int
    {
        Disabled = 0,
        FullAccess = 1 << 30, // int: 2^31 - 1
    }


    public sealed class ProductModel : BaseNodeModel
    {
        public ConcurrentDictionary<Guid, AccessKeyModel> AccessKeys { get; } = new();

        public ConcurrentDictionary<Guid, ProductModel> SubProducts { get; } = new();

        public ConcurrentDictionary<Guid, BaseSensorModel> Sensors { get; } = new();


        public ProductState State { get; }

        public Guid? FolderId { get; private set; }


        public NotificationSettingsEntity NotificationsSettings { get; private set; }


        public ProductModel(string name, Guid? authorId = default) : base(name.Trim(), authorId)
        {
            State = ProductState.FullAccess;
        }

        public ProductModel(ProductEntity entity) : base(entity)
        {
            State = (ProductState)entity.State;
            NotificationsSettings = entity.NotificationSettings;
            FolderId = Guid.TryParse(entity.FolderId, out var folderId) ? folderId : null;
        }


        internal void AddSubProduct(ProductModel product)
        {
            SubProducts.TryAdd(product.Id, (ProductModel)product.AddParent(this));
        }

        internal void AddSensor(BaseSensorModel sensor)
        {
            Sensors.TryAdd(sensor.Id, (BaseSensorModel)sensor.AddParent(this));
        }

        internal ProductModel Update(ProductUpdate update)
        {
            base.Update(update);

            FolderId = update.FolderId.HasValue
                ? update.FolderId != Guid.Empty ? update.FolderId : null
                : FolderId;
            NotificationsSettings = update?.NotificationSettings ?? NotificationsSettings;

            return this;
        }


        internal override bool HasUpdateTimeout()
        {
            var result = false;

            foreach (var (_, sensor) in Sensors)
                result |= sensor.HasUpdateTimeout();

            foreach (var (_, subProduct) in SubProducts)
                result |= subProduct.HasUpdateTimeout();

            return result;
        }

        internal ProductEntity ToProductEntity() =>
            new()
            {
                Id = Id.ToString(),
                AuthorId = AuthorId.ToString(),
                ParentProductId = Parent?.Id.ToString(),
                FolderId = FolderId?.ToString(),
                State = (int)State,
                DisplayName = DisplayName,
                Description = Description,
                CreationDate = CreationDate.Ticks,
                NotificationSettings = NotificationsSettings,
                Policies = GetPolicyIds().Select(u => $"{u}").ToList(),
            };
    }
}
