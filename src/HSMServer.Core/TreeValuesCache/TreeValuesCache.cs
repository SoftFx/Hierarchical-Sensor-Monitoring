using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.DataLayer;
using HSMServer.Core.TreeValuesCache.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

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

            (var products, var sensors, var sensorsData) = GenerateTestData();

            BuildTreeWithMigration(products, sensors, sensorsData);
        }


        public void BuildTree(List<ProductEntity> productEntities,
            List<SensorEntity> sensorEntities, List<SensorDataEntity> sensorDataEntities)
        {
            FillTreeByProductModels(productEntities);

            var sensorDatas = GetSensorDatasDict(sensorDataEntities);
            foreach (var sensorEntity in sensorEntities)
            {
                var sensor = new SensorModel(sensorEntity, sensorDatas[sensorEntity.Id]);
                _sensors.TryAdd(sensor.Id, sensor);
            }

            foreach (var productEntity in productEntities)
                if (_tree.TryGetValue(new Guid(productEntity.Id), out var product))
                {
                    foreach (var subProductId in productEntity.SubProductsIds)
                    {
                        if (_tree.TryGetValue(new Guid(subProductId), out var subProduct))
                            product.AddSubProduct(subProduct);
                    }

                    foreach (var sensorId in productEntity.SensorsIds)
                    {
                        if (_sensors.TryGetValue(new Guid(sensorId), out var sensor))
                            product.AddSensor(sensor);
                    }
                }
        }

        public void BuildTreeWithMigration(List<ProductEntity> productEntities,
            List<SensorEntity> sensorEntities, List<SensorDataEntity> sensorDataEntities)
        {
            FillTreeByProductModels(productEntities);

            var sensorDatas = GetSensorDatasDict(sensorDataEntities);
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

                var sensor = new SensorModel(sensorEntity, sensorDatas[sensorEntity.Id]);
                parentProduct.AddSensor(sensor);

                _sensors.TryAdd(sensor.Id, sensor);
            }
        }

        public List<ProductModel> GetTree() => _tree.Values.ToList();

        public List<SensorModel> GetSensors() => _sensors.Values.ToList();


        private void FillTreeByProductModels(List<ProductEntity> productEntities)
        {
            foreach (var productEntity in productEntities)
            {
                var product = new ProductModel(productEntity);
                _tree.TryAdd(product.Id, product);
            }
        }

        private static Dictionary<string, SensorDataEntity> GetSensorDatasDict(List<SensorDataEntity> sensorDataEntities) =>
            sensorDataEntities.ToDictionary(s => s.Id); // TODO: dictionary (key, list<values>) for several datas

        private static (List<ProductEntity>, List<SensorEntity>, List<SensorDataEntity>) GenerateTestData()
        {
            var products = new List<ProductEntity>();
            var sensors = new List<SensorEntity>();
            var sensorsData = new List<SensorDataEntity>();

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
                var sensor11 = new SensorEntity()
                {
                    Id = Guid.NewGuid().ToString(),
                    ProductName = productName,
                    Path = $"sensor{i}1",
                    SensorType = 7,
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
                sensors.Add(sensor11);
                sensors.Add(sensor2);
                sensors.Add(sensor3);

                var sensorDaata1 = new SensorDataEntity()
                {
                    Id = sensor1.Id,
                    TimeCollected = DateTime.UtcNow,
                    Path = sensor1.Path,
                    DataType = (byte)sensor1.SensorType,
                    TypedData = JsonSerializer.Serialize(new BoolSensorData() { BoolValue = true, Comment = "sensorData1" }),
                    Status = (byte)SensorStatus.Warning,
                };
                var sensorDaata11 = new SensorDataEntity()
                {
                    Id = sensor11.Id,
                    TimeCollected = DateTime.UtcNow,
                    Path = sensor11.Path,
                    DataType = (byte)sensor11.SensorType,
                    TypedData = JsonSerializer.Serialize(new FileSensorBytesData() { FileContent = Encoding.UTF8.GetBytes("123"), FileName = "filename", Extension = "txt", Comment = "sensorData11" }),
                    Status = (byte)SensorStatus.Ok,
                };
                var sensorDaata2 = new SensorDataEntity()
                {
                    Id = sensor2.Id,
                    TimeCollected = DateTime.UtcNow,
                    Path = sensor2.Path,
                    DataType = (byte)sensor2.SensorType,
                    TypedData = JsonSerializer.Serialize(new BoolSensorData() { BoolValue = false, Comment = "sensorData2" }),
                    Status = (byte)SensorStatus.Ok,
                };
                var sensorDaata3 = new SensorDataEntity()
                {
                    Id = sensor3.Id,
                    TimeCollected = DateTime.UtcNow,
                    Path = sensor3.Path,
                    DataType = (byte)sensor3.SensorType,
                    TypedData = JsonSerializer.Serialize(new BoolSensorData() { BoolValue = false, Comment = "sensorData3" }),
                    Status = (byte)SensorStatus.Ok,
                };
                sensorsData.Add(sensorDaata1);
                sensorsData.Add(sensorDaata11);
                sensorsData.Add(sensorDaata2);
                sensorsData.Add(sensorDaata3);

                products.Add(new ProductEntity()
                {
                    Id = Guid.NewGuid().ToString(),
                    DisplayName = productName,
                    SensorsIds = new List<string> { sensor1.Id, sensor11.Id, sensor2.Id, sensor3.Id }
                });
            }

            return (products, sensors, sensorsData);
        }
    }
}
