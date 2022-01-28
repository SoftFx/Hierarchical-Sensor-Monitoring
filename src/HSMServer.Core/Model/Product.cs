using HSMCommon.Attributes;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    [SwaggerIgnore]
    public class Product
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public DateTime DateAdded { get; set; }
        public List<ExtraProductKey> ExtraKeys { get; set; }
        public ConcurrentDictionary<string, SensorInfo> Sensors { get; private set; }

        public Product()
        {
            ExtraKeys = new List<ExtraProductKey>();
            Sensors = new ConcurrentDictionary<string, SensorInfo>();
        }
        public Product(string key, string name, DateTime dateAdded) : this()
        {
            Key = key;
            Name = name;
            DateAdded = dateAdded;
        }
        public Product(Product product)
        {
            if (product == null) return;

            Key = product.Key;
            Name = product.Name;
            DateAdded = product.DateAdded;
            ExtraKeys = new List<ExtraProductKey>();
            Sensors = new ConcurrentDictionary<string, SensorInfo>();

            if (product.ExtraKeys != null && product.ExtraKeys.Count > 0)
                ExtraKeys.AddRange(product.ExtraKeys);
        }

        public Product(ProductEntity entity)
        {
            if (entity == null) return;

            Key = entity.Key;
            Name = entity.Name;
            DateAdded = entity.DateAdded;
            ExtraKeys = new List<ExtraProductKey>();
            Sensors = new ConcurrentDictionary<string, SensorInfo>();

            if (entity.ExtraKeys != null && entity.ExtraKeys.Count > 0)
            {
                ExtraKeys.AddRange(entity.ExtraKeys.Select(e => new ExtraProductKey(e)));
            }
        }

        public void Update(Product product)
        {
            if (this == product) return; 

            ExtraKeys = new List<ExtraProductKey>();
            if (product.ExtraKeys != null && product.ExtraKeys.Count > 0)
            {
                ExtraKeys.AddRange(product.ExtraKeys);
            }
        }

        public void InitializeSensors(List<SensorInfo> sensors)
        {
            Sensors = new ConcurrentDictionary<string, SensorInfo>();

            sensors.ForEach(s => Sensors[s.Path] = s);
        }

        public void AddOrUpdateSensor(SensorInfo sensor) => 
            Sensors[sensor.Path] = sensor;

        public bool RemoveSensor(string path) =>
            Sensors.TryRemove(path, out var sensor);
        
    }
}
