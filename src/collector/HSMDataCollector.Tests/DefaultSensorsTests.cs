using HSMDataCollector.Core;
using System;
using Xunit;

namespace HSMDataCollector.Tests
{
    public class DefaultSensorsTests : IDisposable
    {
        private const string CurrentProcessNodeName = "CurrentProcess";
        private const string ProductName = "TestProduct";

        private readonly DataCollector _dataCollector;


        public DefaultSensorsTests()
        {
            _dataCollector = new DataCollector(ProductName, "https://localhost");
            _dataCollector.Initialize(true, null, null);
        }


        [Fact]
        [Trait("Category", "Create default sensor (Process CPU)")]
        public void CreateDefaultSensorTest()
        {
            _dataCollector.InitializeProcessMonitoring(true, false, false);

            Assert.True(_dataCollector.IsSensorExists($"{CurrentProcessNodeName}/Process CPU"));
        }

        [Fact]
        [Trait("Category", "Create default sensor (Process CPU)")]
        public void CreateDefaultSensor_WithSpecificPath_Test()
        {
            const string specificPath = "Specific path/123";

            _dataCollector.InitializeProcessMonitoring(true, false, false, specificPath);

            Assert.True(_dataCollector.IsSensorExists($"{specificPath}/Process CPU"));
            Assert.False(_dataCollector.IsSensorExists($"{CurrentProcessNodeName}/Process CPU"));
        }


        public void Dispose() => _dataCollector.Stop();
    }
}
