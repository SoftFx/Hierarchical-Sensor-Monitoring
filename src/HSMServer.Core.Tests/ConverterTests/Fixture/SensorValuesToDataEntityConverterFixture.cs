using HSMServer.Core.Tests.Infrastructure;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class EntitiesConverterFixture
    {
        internal const string ProductName = TestProductsManager.ProductName;

        internal SensorValuesFactory SensorValuesFactory { get; }

        internal SensorValuesTester SensorValuesTester { get; }


        public EntitiesConverterFixture()
        {
            SensorValuesFactory = new SensorValuesFactory(ProductName);
            SensorValuesTester = new SensorValuesTester(ProductName);
        }
    }
}
