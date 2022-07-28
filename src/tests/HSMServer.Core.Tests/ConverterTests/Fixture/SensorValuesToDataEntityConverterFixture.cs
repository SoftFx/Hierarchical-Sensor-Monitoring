using HSMServer.Core.Tests.Infrastructure;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class EntitiesConverterFixture
    {
        internal const string ProductName = TestProductsManager.ProductName;

        internal ApiSensorValuesFactory SensorValuesFactory { get; }


        public EntitiesConverterFixture()
        {
            SensorValuesFactory = new ApiSensorValuesFactory(ProductName);
        }
    }
}
