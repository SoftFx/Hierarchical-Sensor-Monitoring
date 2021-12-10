using HSMServer.Core.Tests.MonitoringDataReceiverTests;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class SensorValuesToDataEntityConverterFixture
    {
        private const string ProductKey = "TestProduct";

        internal SensorValuesFactory SensorValuesFactory { get; }


        public SensorValuesToDataEntityConverterFixture()
        {
            SensorValuesFactory = new SensorValuesFactory(ProductKey);
        }
    }
}
