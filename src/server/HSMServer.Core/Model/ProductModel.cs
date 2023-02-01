﻿using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Concurrent;

namespace HSMServer.Core.Model
{
    [Flags]
    public enum ProductState : int
    {
        Disabled = 0,
        FullAccess = 1 << 30, // int: 2^31 - 1
    }


    public sealed class ProductModel : NodeBaseModel, INotificatable
    {
        public ProductState State { get; }

        public ConcurrentDictionary<Guid, AccessKeyModel> AccessKeys { get; }

        public ConcurrentDictionary<Guid, ProductModel> SubProducts { get; }

        public ConcurrentDictionary<Guid, BaseSensorModel> Sensors { get; }

        public NotificationSettings NotificationsSettings { get; }


        string INotificatable.Name => DisplayName;

        NotificationSettings INotificatable.Notifications => NotificationsSettings;

        bool INotificatable.AreNotificationsEnabled(BaseSensorModel sensor) =>
            NotificationsSettings.Telegram.MessagesAreEnabled &&
            NotificationsSettings.IsSensorEnabled(sensor.Id) &&
            !NotificationsSettings.IsSensorIgnored(sensor.Id);


        public ProductModel()
        {
            AccessKeys = new ConcurrentDictionary<Guid, AccessKeyModel>();
            SubProducts = new ConcurrentDictionary<Guid, ProductModel>();
            Sensors = new ConcurrentDictionary<Guid, BaseSensorModel>();
            NotificationsSettings = new();
        }

        public ProductModel(ProductEntity entity) : this()
        {
            Id = Guid.TryParse(entity.Id, out var entityId) ? entityId : Guid.NewGuid(); // TODO: remove Guid.NewGuid() after removing prosuctId string -> Guid migration
            AuthorId = Guid.TryParse(entity.AuthorId, out var authorId) ? authorId : null;
            State = (ProductState)entity.State;
            DisplayName = entity.DisplayName;
            Description = entity.Description;
            CreationDate = new DateTime(entity.CreationDate);
            NotificationsSettings = new(entity.NotificationSettings);
        }

        public ProductModel(string name) : this()
        {
            Id = Guid.NewGuid();
            State = ProductState.FullAccess;
            DisplayName = name;
            CreationDate = DateTime.UtcNow;
        }


        internal bool AddAccessKey(AccessKeyModel key) => AccessKeys.TryAdd(key.Id, key);

        internal void AddSubProduct(ProductModel product)
        {
            product.ParentProduct = this;

            SubProducts.TryAdd(product.Id, product);
        }

        internal void AddSensor(BaseSensorModel sensor)
        {
            sensor.ParentProduct = this;

            Sensors.TryAdd(sensor.Id, sensor);
        }

        internal ProductEntity ToProductEntity() =>
            new()
            {
                Id = Id.ToString(),
                AuthorId = AuthorId.ToString(),
                ParentProductId = ParentProduct?.Id.ToString(),
                State = (int)State,
                DisplayName = DisplayName,
                Description = Description,
                CreationDate = CreationDate.Ticks,
                NotificationSettings = NotificationsSettings.ToEntity(),
                Policies = GetPolicyIds(),
            };


        internal override void BuildProductNameAndPath()
        {
            if (ParentProduct == null)
            {
                RootProductId = Id;
                RootProductName = DisplayName;
            }
            else
                base.BuildProductNameAndPath();

            foreach (var (_, sensor) in Sensors)
                sensor.BuildProductNameAndPath();

            foreach (var (_, subProduct) in SubProducts)
                subProduct.BuildProductNameAndPath();
        }


        internal override void RefreshOutdatedError() =>
            UpdateChildSensorsValidationResult(this);

        private static void UpdateChildSensorsValidationResult(ProductModel product)
        {
            foreach (var (_, sensor) in product.Sensors)
                if (sensor.ExpectedUpdateInterval == null)
                    sensor.RefreshOutdatedError();

            foreach (var (_, subProduct) in product.SubProducts)
                if (subProduct.ExpectedUpdateInterval == null)
                    UpdateChildSensorsValidationResult(subProduct);
        }
    }
}
