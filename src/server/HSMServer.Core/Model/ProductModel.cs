using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
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


        public override ProductPolicyCollection Policies { get; } = new();


        public ProductState State { get; }

        public Guid? FolderId { get; private set; }


        public NotificationSettingsEntity NotificationsSettings { get; private set; }


        public ProductModel(string name, Guid? authorId = default) : base(name.Trim(), authorId)
        {
            State = ProductState.FullAccess;

            Policies.BuildDefault(this);
        }

        public ProductModel(ProductEntity entity) : base(entity)
        {
            State = (ProductState)entity.State;
            NotificationsSettings = entity.NotificationSettings;
            FolderId = Guid.TryParse(entity.FolderId, out var folderId) ? folderId : null;

            Policies.BuildDefault(this, entity.TTLPolicy);
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

            if (update.FolderId is not null)
                FolderId = update.FolderId != Guid.Empty ? update.FolderId : null;

            NotificationsSettings = update?.NotificationSettings ?? NotificationsSettings;

            return this;
        }

        internal override bool CheckTimeout()
        {
            var result = false;

            foreach (var (_, sensor) in Sensors)
                result |= sensor.CheckTimeout();

            foreach (var (_, subProduct) in SubProducts)
                result |= subProduct.CheckTimeout();

            return result;
        }

        internal ProductEntity ToEntity() => new()
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
            Policies = Policies.Ids.Select(u => $"{u}").ToList(),
            Settings = Settings.ToEntity(),
            TTLPolicy = Policies.TimeToLive?.ToEntity(),
            ChangeInfo = _changeTable.ToEntity(),
        };


        protected override void UpdateTTL(PolicyUpdate update)
        {
            static void UpdateTTLPolicy(ProductModel model, PolicyUpdate update)
            {
                model.Policies.TimeToLive.Update(update);

                foreach (var (_, subProduct) in model.SubProducts)
                    UpdateTTLPolicy(subProduct, update);

                foreach (var (_, sensor) in model.Sensors)
                    if (!sensor.Settings.TTL.IsSet)
                    {
                        sensor.Policies.TimeToLive.Update(update);
                        sensor.UpdateFromParentSettings?.Invoke(sensor.ToEntity());
                    }
            }

            UpdateTTLPolicy(this, update);
        }
    }
}