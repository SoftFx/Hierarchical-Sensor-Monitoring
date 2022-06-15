using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
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
    }
}
