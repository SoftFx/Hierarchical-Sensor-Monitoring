using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace HSMServer.Core.Cache.Entities
{
    [Flags]
    public enum ProductState : int
    {
        Disabled = 0,
        FullAccess = 1 << 62,
    }


    public sealed class ProductModel
    {
        public string Id { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public ProductState State { get; }

        public DateTime CreationDate { get; }

        public ConcurrentDictionary<string, ProductModel> SubProducts { get; }

        public ConcurrentDictionary<Guid, SensorModel> Sensors { get; }

        public ProductModel ParentProduct { get; private set; }


        public ProductModel()
        {
            SubProducts = new ConcurrentDictionary<string, ProductModel>();
            Sensors = new ConcurrentDictionary<Guid, SensorModel>();
        }

        public ProductModel(ProductEntity entity) : this()
        {
            Id = entity.Id;
            State = (ProductState)entity.State;
            DisplayName = entity.DisplayName;
            Description = entity.Description;
            CreationDate = new DateTime(entity.CreationDate);
        }

        public ProductModel(string name, ProductModel parent = null) : this()
        {
            Id = Guid.NewGuid().ToString();
            ParentProduct = parent;
            State = ProductState.FullAccess;
            DisplayName = name;
            CreationDate = DateTime.UtcNow;
        }


        internal void AddSubProduct(ProductModel product)
        {
            product.ParentProduct = this;

            SubProducts.TryAdd(product.Id, product);
        }

        internal void AddSensor(SensorModel sensor)
        {
            sensor.ParentProduct = this;

            Sensors.TryAdd(sensor.Id, sensor);
        }

        internal ProductEntity ToProductEntity() =>
            new()
            {
                Id = Id,
                //AuthorId ???
                ParentProductId = ParentProduct?.Id ?? string.Empty,
                State = (int)State,
                DisplayName = DisplayName,
                Description = Description,
                CreationDate = CreationDate.Ticks,
                SubProductsIds = SubProducts.Select(p => p.Value.Id).ToList(),
                SensorsIds = Sensors.Select(p => p.Value.Id.ToString()).ToList(),
            };
    }
}
