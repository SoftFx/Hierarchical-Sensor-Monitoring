using HSMServer.Core.Tests.Infrastructure;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class EntitiesConverterFixture
    {
        internal const string ProductKey = "TestProduct";

        internal SensorValuesFactory SensorValuesFactory { get; }

        internal SensorValuesTester SensorValuesTester { get; }


        public EntitiesConverterFixture()
        {
            SensorValuesFactory = new SensorValuesFactory(ProductKey);
            SensorValuesTester = new SensorValuesTester(ProductKey);
        }
    }
}
