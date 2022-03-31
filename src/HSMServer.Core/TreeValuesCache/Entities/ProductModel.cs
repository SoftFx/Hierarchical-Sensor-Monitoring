using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Concurrent;

namespace HSMServer.Core.TreeValuesCache.Entities
{
    [Flags]
    public enum ProductState : long
    {
        Disabled = 0,
        Enabled = 1 << 62,
    }


    public class ProductModel
    {
        public Guid Id { get; }
        public Guid? ParentProductId { get; private set; }
        public ProductModel ParentProduct { get; private set; }
        public ProductState State { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public DateTime CreationDate { get; }
        public ConcurrentDictionary<Guid, ProductModel> SubProducts { get; }
        public ConcurrentDictionary<Guid, SensorModel> Sensors { get; }


        public ProductModel()
        {
            SubProducts = new ConcurrentDictionary<Guid, ProductModel>();
            Sensors = new ConcurrentDictionary<Guid, SensorModel>();
        }

        public ProductModel(ProductEntity entity) : this()
        {
            Id = new Guid(entity.Id);// entity.Id == null ? Guid.NewGuid() : new Guid(entity.Id);
            State = (ProductState)entity.State;
            DisplayName = entity.DisplayName;// ?? entity.Name; // TODO: remove '?? entity.Name'
            Description = entity.Description;
            CreationDate = new DateTime(entity.CreationDate);
        }

        public ProductModel(string name, ProductModel parent) : this()
        {
            Id = Guid.NewGuid();
            ParentProductId = parent.Id;
            ParentProduct = parent;
            State = ProductState.Enabled;
            DisplayName = name;
            CreationDate = DateTime.UtcNow;
        }


        internal void AddSubProduct(ProductModel product)
        {
            product.ParentProductId = this.Id;
            product.ParentProduct = this;

            SubProducts.TryAdd(product.Id, product);
        }

        internal void AddSensor(SensorModel sensor)
        {
            sensor.ParentProduct = this;

            Sensors.TryAdd(sensor.ParentProduct.Id, sensor);
        }
    }
}
