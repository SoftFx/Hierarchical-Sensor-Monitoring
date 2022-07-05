using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace HSMServer.Core.Cache.Entities
{
    [Flags]
    public enum ProductState : int
    {
        Disabled = 0,
        FullAccess = 1 << 30, // int: 2^31 - 1
    }


    public sealed class ProductModel
    {
        public string Id { get; }

        public string AuthorId { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public ProductState State { get; }

        public DateTime CreationDate { get; }

        public ConcurrentDictionary<Guid, AccessKeyModel> AccessKeys { get; }

        public ConcurrentDictionary<string, ProductModel> SubProducts { get; }

        public ConcurrentDictionary<Guid, BaseSensorModel> Sensors { get; }

        public ProductModel ParentProduct { get; private set; }


        public ProductModel()
        {
            AccessKeys = new ConcurrentDictionary<Guid, AccessKeyModel>();
            SubProducts = new ConcurrentDictionary<string, ProductModel>();
            Sensors = new ConcurrentDictionary<Guid, BaseSensorModel>();
        }

        public ProductModel(ProductEntity entity) : this()
        {
            Id = entity.Id;
            AuthorId = entity.AuthorId;
            State = (ProductState)entity.State;
            DisplayName = entity.DisplayName;
            Description = entity.Description;
            CreationDate = new DateTime(entity.CreationDate);
        }

        public ProductModel(string name) : this()
        {
            Id = Guid.NewGuid().ToString();
            State = ProductState.FullAccess;
            DisplayName = name;
            CreationDate = DateTime.UtcNow;
        }

        public ProductModel(string key, string name) : this(name)
        {
            Id = key;
        }

        internal bool AddAccessKey(AccessKeyModel key) => AccessKeys.TryAdd(key.Id, key);

        internal void AddSubProduct(ProductModel product)
        {
            product.ParentProduct = this;

            SubProducts.TryAdd(product.Id, product);
        }

        internal void AddSensor(BaseSensorModel sensor)
        {
            sensor.SetProduct(Id);

            Sensors.TryAdd(sensor.Id, sensor);
        }

        internal ProductEntity ToProductEntity() =>
            new()
            {
                Id = Id,
                AuthorId = AuthorId,
                ParentProductId = ParentProduct?.Id,
                State = (int)State,
                DisplayName = DisplayName,
                Description = Description,
                CreationDate = CreationDate.Ticks,
                SubProductsIds = SubProducts.Select(p => p.Value.Id).ToList(),
                SensorsIds = Sensors.Select(p => p.Value.Id.ToString()).ToList(),
            };
    }
}
