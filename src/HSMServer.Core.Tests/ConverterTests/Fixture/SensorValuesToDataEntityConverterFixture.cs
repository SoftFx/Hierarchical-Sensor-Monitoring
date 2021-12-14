using HSMServer.Core.Tests.MonitoringDataReceiverTests;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class SensorValuesToDataEntityConverterFixture
    {
        internal const string ProductKey = "TestProduct";

        internal SensorValuesFactory SensorValuesFactory { get; }

        internal SensorValuesTester SensorValuesTester { get; }


        public SensorValuesToDataEntityConverterFixture()
        {
            SensorValuesFactory = new SensorValuesFactory(ProductKey);
            SensorValuesTester = new SensorValuesTester(ProductKey);
        }
    }
}
