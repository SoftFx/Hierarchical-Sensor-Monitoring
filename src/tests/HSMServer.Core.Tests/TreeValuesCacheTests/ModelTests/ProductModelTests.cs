using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Tests.Infrastructure;
using System;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests.ModelTests
{
    public class ProductModelTests
    {
        [Fact]
        [Trait("Category", "ProductModel constructor")]
        public void ProductModelConstructor_ProductEntity_Test()
        {
            var productEntity = EntitiesFactory.BuildProductEntity(parent: null);

            var product = new ProductModel(productEntity);

            ModelsTester.TestProductModel(productEntity, product);
        }

        [Fact]
        [Trait("Category", "ProductModel constructor")]
        public void ProductModelConstructor_OnlyName_Test()
        {
            var product = new ProductModel(TestProductsManager.ProductName);

            ModelsTester.TestProductModel(TestProductsManager.ProductName, product);
        }

        [Fact]
        [Trait("Category", "ProductModel constructor")]
        public void ProductModelConstructor_KeyAndName_Test()
        {
            var key = TestProductsManager.TestProduct.Id;
            var name = TestProductsManager.TestProduct.DisplayName;

            var product = new ProductModel(key, name);

            Assert.Equal(key, product.Id);
            ModelsTester.TestProductModel(name, product);
        }


        //[Fact]
        //[Trait("Category", "ProductModel to ProductEntity")]
        //public void ProductModelToProductEntityTest()
        //{
        //    var subProductId = Guid.NewGuid().ToString();
        //    var sensor1Id = Guid.NewGuid().ToString();
        //    var sensor2Id = Guid.NewGuid().ToString();
        //    var entity = EntitiesFactory.BuildProductEntity(parent: null)
        //                                .AddSubProduct(subProductId)
        //                                .AddSensor(sensor1Id)
        //                                .AddSensor(sensor2Id);

        //    var product = new ProductModel(entity);
        //    product.AddSubProduct(new ProductModel(subProductId, RandomGenerator.GetRandomString()));
        //    product.AddSensor(new SensorModel(new SensorEntity() { Id = sensor1Id }, null));
        //    product.AddSensor(new SensorModel(new SensorEntity() { Id = sensor2Id }, null));

        //    var productEntity = product.ToProductEntity();

        //    ModelsTester.TestProductModel(productEntity, product);
        //}
    }
}
