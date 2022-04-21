using HSMCommon.Attributes;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Model
{
    [SwaggerIgnore]
    public class Product
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public DateTime DateAdded { get; set; }
        [Obsolete]
        public List<ExtraProductKey> ExtraKeys { get; set; }
        [JsonIgnore]
        public ConcurrentDictionary<string, SensorInfo> Sensors { get; }

        public Product()
        {
            DateAdded = DateTime.UtcNow;
            ExtraKeys = new List<ExtraProductKey>();
            Sensors = new ConcurrentDictionary<string, SensorInfo>();
        }

        public Product(string key, string name) : this()
        {
            Id = key;
            DisplayName = name;
        }

        public Product(Product product) : this()
        {
            if (product == null) return;

            Id = product.Id;
            DisplayName = product.DisplayName;
            DateAdded = product.DateAdded;
            AddExtraKeys(product.ExtraKeys);
        }

        //todo update ?
        public Product(ProductEntity entity) : this()
        {
            if (entity == null) return;

            Id = entity.Id;
            DisplayName = entity.DisplayName;
            DateAdded = new DateTime(entity.DateAdded);
            ExtraKeys = new List<ExtraProductKey>();
        }

        public void Update(Product product)
        {
            if (this == product) return; 

            ExtraKeys = new List<ExtraProductKey>();
            AddExtraKeys(product.ExtraKeys);
        }

        public void InitializeSensors(List<SensorInfo> sensors) =>
            sensors.ForEach(s => Sensors[s.Path] = s);

        public void AddOrUpdateSensor(SensorInfo sensor) => 
            Sensors[sensor.Path] = sensor;

        public bool RemoveSensor(string path) =>
            Sensors.TryRemove(path, out _);

        public void AddExtraKeys(List<ExtraProductKey> keys)
        {
            if (keys != null && keys.Count > 0)
                ExtraKeys.AddRange(keys);
        }
    }
}
