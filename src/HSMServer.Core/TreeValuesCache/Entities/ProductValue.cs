using HSMDatabase.AccessManager.DatabaseEntities;
using System;
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
        public Guid Id { get; init; }
        public Guid? ParentProductId { get; set; }
        public ProductState State { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public DateTime CreationDate { get; set; }
        public Dictionary<Guid, ProductValue> SubProducts { get; set; }
        public Dictionary<Guid, SensorValue> Sensors { get; set; }


        public ProductValue(ProductEntity entity)
        {
            Id = entity.Id == null ? Guid.NewGuid() : new Guid(entity.Id);
            ParentProductId = entity.ParentProductId == null ? Guid.NewGuid() : new Guid(entity.ParentProductId);
            State = (ProductState)entity.State;
            DisplayName = entity.DisplayName ?? entity.Name;
            Description = entity.Description;
            CreationDate = entity.CreationDate == 0 ? entity.DateAdded : new DateTime(entity.CreationDate);
            SubProducts = new Dictionary<Guid, ProductValue>();
            Sensors = new Dictionary<Guid, SensorValue>();
        }


        internal void AddSubProduct(ProductValue product)
        {
            product.ParentProductId = Id;

            SubProducts.Add(product.Id, product);
        }

        internal void AddSensor(SensorValue sensor)
        {
            sensor.ParentProductId = Id;

            Sensors.Add(sensor.ParentProductId, sensor);
        }
    }
}
