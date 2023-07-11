using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using System;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests.ModelTests
{
    public class AccessKeyModelTests
    {
        [Fact]
        [Trait("Category", "AccessKeyModel constructor")]
        public void AccessKeyModelConstructor_AccessKeyEntity_Test()
        {
            var accessKeyEntity = EntitiesFactory.BuildAccessKeyEntity();

            var accessKey = new AccessKeyModel(accessKeyEntity);

            ModelsTester.TestAccessKeyModel(accessKeyEntity, accessKey);
        }

        [Fact]
        [Trait("Category", "AccessKeyModel constructor")]
        public void AccessKeyModelConstructor_AuthorIdProductId_Test()
        {
            var authorId = Guid.NewGuid();
            var productId = Guid.NewGuid();

            var key = new AccessKeyModel(authorId, productId);

            ModelsTester.TestAccessKeyModel(authorId, productId, key);
        }

        [Fact]
        [Trait("Category", "AccessKeyModel from ProductModel")]
        public void AccessKeyModel_From_ProductModel()
        {
            var productEntity = EntitiesFactory.BuildProductEntity();
            var product = new ProductModel(productEntity);

            var key = AccessKeyModel.BuildDefault(product);

            ModelsTester.TestAccessKeyModel(product, key);
        }

        [Fact]
        [Trait("Category", "AccessKeyModel to AccessKeyEntity")]
        public void AccessKeyModelToAccessKeyEntityTest()
        {
            var entity = EntitiesFactory.BuildAccessKeyEntity();

            var key = new AccessKeyModel(entity);

            var accessKeyEntity = key.ToAccessKeyEntity();

            ModelsTester.TestAccessKeyModel(accessKeyEntity, key);
        }
    }
}
