using HSMCommon.Attributes;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Model
{
    [SwaggerIgnore]
    public class Product
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public DateTime CreationDate { get; set; }
        [JsonIgnore]
        public ConcurrentDictionary<string, SensorInfo> Sensors { get; }

        public Product()
        {
            CreationDate = DateTime.UtcNow;
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
            CreationDate = product.CreationDate;
        }

        //todo update ?
        public Product(ProductEntity entity) : this()
        {
            if (entity == null) return;

            Id = entity.Id;
            DisplayName = entity.DisplayName;
            CreationDate = new DateTime(entity.CreationDate);
        }

        public void InitializeSensors(List<SensorInfo> sensors) =>
            sensors.ForEach(s => Sensors[s.Path] = s);

        public void AddOrUpdateSensor(SensorInfo sensor) =>
            Sensors[sensor.Path] = sensor;

        public bool RemoveSensor(string path) =>
            Sensors.TryRemove(path, out _);
    }
}
