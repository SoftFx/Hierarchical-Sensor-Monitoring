using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.Core.TreeValuesCache.Entities
{
    public enum ProductState
    {
        Enabled,
        Disabled,
    }

    public class ProductValue
    {
        public Guid Id { get; }
        public ProductValue ParentProduct { get; private set; }
        public ProductState State { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public DateTime CreationDate { get; }
        public ConcurrentDictionary<Guid, ProductValue> SubProducts { get; }
        public ConcurrentDictionary<Guid, SensorValue> Sensors { get; }


        public ProductValue(ProductEntity entity)
        {
            Id = entity.Id == null ? Guid.NewGuid() : new Guid(entity.Id);
            State = (ProductState)entity.State;
            DisplayName = entity.DisplayName ?? entity.Name; // TODO: remove '?? entity.Name'
            Description = entity.Description;
            CreationDate = new DateTime(entity.CreationDate);
            SubProducts = new ConcurrentDictionary<Guid, ProductValue>();
            Sensors = new ConcurrentDictionary<Guid, SensorValue>();
        }


        internal void AddSubProduct(ProductValue product)
        {
            product.ParentProduct = this;

            SubProducts.TryAdd(product.Id, product);
        }

        internal void AddSensor(SensorValue sensor)
        {
            sensor.ParentProduct = this;

            Sensors.TryAdd(sensor.ParentProduct.Id, sensor);
        }
    }
}
