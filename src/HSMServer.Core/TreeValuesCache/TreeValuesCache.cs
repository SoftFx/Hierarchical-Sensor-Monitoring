using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.TreeValuesCache.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.TreeValuesCache
{
    public interface ITreeValuesCache
    {
        List<ProductModel> GetTree();
    }

    public sealed class TreeValuesCache : ITreeValuesCache
    {
        private readonly ConcurrentDictionary<Guid, ProductModel> _tree;
        private readonly ConcurrentDictionary<Guid, SensorModel> _sensors;


        public TreeValuesCache(IDatabaseAdapter database)
        {
            _tree = new ConcurrentDictionary<Guid, ProductModel>();
            _sensors = new ConcurrentDictionary<Guid, SensorModel>();

            var products = new List<ProductEntity>();
            var sensors = new List<SensorEntity>();

            for (int i = 0; i < 2; ++i)
            {
                string productName = $"product{i}";

                var sensor1 = new SensorEntity()
                {
                    Id = Guid.NewGuid().ToString(),
                    ProductName = productName,
                    Path = $"sensor{i}",
                    SensorType = 0,
                };
                var sensor2 = new SensorEntity()
                {
                    Id = Guid.NewGuid().ToString(),
                    ProductName = productName,
                    Path = $"subProduct/sensor{i}",
                    SensorType = 0,
                };
                var sensor3 = new SensorEntity()
                {
                    Id = Guid.NewGuid().ToString(),
                    ProductName = productName,
                    Path = $"subProduct/subSubProduct/sensor{i}",
                    SensorType = 0,
                };
                sensors.Add(sensor1);
                sensors.Add(sensor2);
                sensors.Add(sensor3);

                products.Add(new ProductEntity()
                {
                    Id = Guid.NewGuid().ToString(),
                    DisplayName = productName,
                    SensorsIds = new List<string> { sensor1.Id, sensor2.Id, sensor3.Id }
                });
            }

            BuildTreeWithMigration(products, sensors);
        }


        public void BuildTreeWithMigration(List<ProductEntity> productEntities, List<SensorEntity> sensorEntities)
        {
            foreach (var productEntity in productEntities)
            {
                var product = new ProductModel(productEntity);
                _tree.TryAdd(product.Id, product);
            }

            foreach (var sensorEntity in sensorEntities)
            {
                var parentProduct = _tree.FirstOrDefault(p => p.Value.DisplayName == sensorEntity.ProductName).Value;

                var pathParts = sensorEntity.Path.Split(CommonConstants.SensorPathSeparator);
                for (int i = 0; i < pathParts.Length - 1; ++i)
                {
                    var subProductName = pathParts[i];
                    var subProduct = parentProduct.SubProducts.FirstOrDefault(p => p.Value.DisplayName == subProductName).Value;
                    if (subProduct == null)
                    {
                        subProduct = new ProductModel(subProductName, parentProduct);
                        parentProduct.AddSubProduct(subProduct);

                        _tree.TryAdd(subProduct.Id, subProduct);
                    }

                    parentProduct = subProduct;
                }

                var sensor = new SensorModel(sensorEntity);
                parentProduct.AddSensor(sensor);

                _sensors.TryAdd(sensor.Id, sensor);
            }
        }

        public List<ProductModel> GetTree() => _tree.Values.ToList();
    }
}
