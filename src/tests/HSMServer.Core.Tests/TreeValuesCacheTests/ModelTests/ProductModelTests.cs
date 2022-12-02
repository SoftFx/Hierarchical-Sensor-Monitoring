using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using System;
using Xunit;
using SensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;

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


        [Fact]
        [Trait("Category", "ProductModel to ProductEntity")]
        public void ProductModelToProductEntityTest()
        {
            var subProductId = Guid.NewGuid().ToString();
            var sensor1Id = Guid.NewGuid().ToString();
            var sensor2Id = Guid.NewGuid().ToString();
            var entity = EntitiesFactory.BuildProductEntity(parent: null);

            var product = new ProductModel(entity);
            product.AddSubProduct(new ProductModel(subProductId, RandomGenerator.GetRandomString()));
            product.AddSensor(SensorModelFactory.Build(new SensorEntity() { Id = sensor1Id }));
            product.AddSensor(SensorModelFactory.Build(new SensorEntity() { Id = sensor2Id }));

            var productEntity = product.ToProductEntity();

            ModelsTester.TestProductModel(productEntity, product);
        }
    }
}
