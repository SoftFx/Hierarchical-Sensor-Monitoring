using System;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Fixtures;

namespace HSMDataCollector.IntegrationTests.Helpers
{
    public static class CollectorOptionsHelper
    {
        public static readonly TimeSpan FastCollectPeriod = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(30);

        public static CollectorOptions CreateTestOptions(HsmServerFixture fixture, string clientName = null)
        {
            return fixture.CreateCollectorOptions(clientName);
        }

        public static string UniqueSensorPath(string sensorName)
        {
            return $"test/{Guid.NewGuid():N}/{sensorName}";
        }

        public static string ServerPath(CollectorOptions options, string sensorPath)
        {
            return $"{options.ComputerName}/{options.Module}/{sensorPath}";
        }
    }
}
