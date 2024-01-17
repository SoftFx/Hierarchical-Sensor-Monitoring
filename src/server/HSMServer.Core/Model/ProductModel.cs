using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
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


    public sealed class ProductModel : BaseNodeModel
    {
        public ConcurrentDictionary<Guid, AccessKeyModel> AccessKeys { get; } = new();

        public ConcurrentDictionary<Guid, ProductModel> SubProducts { get; } = new();

        public ConcurrentDictionary<Guid, BaseSensorModel> Sensors { get; } = new();


        public override ProductPolicyCollection Policies { get; } = new();


        public ProductState State { get; }

        public Guid? FolderId { get; private set; }


        public bool IsEmpty => SubProducts.IsEmpty && Sensors.IsEmpty;


        public ProductModel(string name, Guid? authorId = default) : base(name.Trim(), authorId)
        {
            State = ProductState.FullAccess;

            Policies.BuildDefault(this);
        }

        public ProductModel(ProductEntity entity) : base(entity)
        {
            State = (ProductState)entity.State;
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

            sensor.Policies.Uploaded += Policies.ReceivePolicyUpdate;

            foreach (var policy in sensor.Policies)
                Policies.AddPolicy(policy);
        }

        internal void RemoveSensor(Guid sensorId)
        {
            if (Sensors.TryRemove(sensorId, out var sensor))
                sensor.Policies.Uploaded -= Policies.ReceivePolicyUpdate;
        }

        internal ProductModel Update(ProductUpdate update)
        {
            base.Update(update);

            if (update.FolderId is not null)
                FolderId = update.FolderId != Guid.Empty ? update.FolderId : null;

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
            Settings = Settings.ToEntity(),
            TTLPolicy = Policies.TimeToLive?.ToEntity(),
            ChangeTable = ChangeTable.ToEntity(),
        };


        protected override void UpdateTTL(PolicyUpdate update)
        {
            var parentRequest = update with { IsParentRequest = true };

            void UpdateTTLPolicy(ProductModel model)
            {
                model.Policies.UpdateTTL(model == this ? update : parentRequest);

                foreach (var (_, subProduct) in model.SubProducts)
                    if (!subProduct.Settings.TTL.IsSet)
                        UpdateTTLPolicy(subProduct);

                foreach (var (_, sensor) in model.Sensors)
                    if (!sensor.Settings.TTL.IsSet)
                    {
                        sensor.Policies.UpdateTTL(parentRequest);
                        sensor.UpdateFromParentSettings?.Invoke(sensor.ToEntity());
                    }
            }

            UpdateTTLPolicy(this);
        }
    }
}