using HSMServer.Core.Tests.Infrastructure;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class EntitiesConverterFixture
    {
        internal const string ProductName = TestProductsManager.ProductName;

        internal ApiSensorValuesFactory ApiSensorValuesFactory { get; }


        public EntitiesConverterFixture()
        {
            ApiSensorValuesFactory = new ApiSensorValuesFactory(ProductName);
        }
    }
}
