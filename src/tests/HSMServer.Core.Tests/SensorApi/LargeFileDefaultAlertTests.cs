using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.SensorApi;
using HSMServer.ServerConfiguration;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    public sealed class LargeFileDefaultAlertFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(LargeFileDefaultAlertTests);
    }


    // The default alert of a File sensor registered for large files (#1886): a registration with
    // "DefaultAlertsOptions":4 that creates a File sensor gives it "new data has been received", stored like any
    // policy; nothing else does. Through the real Sensor API over the real cache and database.
    public class LargeFileDefaultAlertTests : MonitoringCoreTestsBase<LargeFileDefaultAlertFixture>
    {
        private const int FileType = 6;
        private const int IntegerType = 1;

        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private readonly string _key = TestProductsManager.TestProductKey.Id;


        public LargeFileDefaultAlertTests(LargeFileDefaultAlertFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) { }


        [Fact]
        public async Task AFileSensorCreatedForLargeFiles_GetsNewData_StoredWithIt()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            await RegisterAsync(host, "large/dumps", FileType, 4);

            var sensor = Sensor("large/dumps");
            var policy = Assert.Single(sensor.Policies);

            Assert.Equal(SensorPolicyCollection.LargeFileAlertTemplate, policy.Template);
            Assert.Equal(PolicyProperty.NewSensorData, Assert.Single(policy.Conditions).Property);
            Assert.True(policy.Destination.IsFromParentChats);

            // Stored: the database holds it under the sensor, as a restart reads it.
            Assert.Contains(_databaseCoreManager.DatabaseCore.GetAllPolicies(), entity => new Guid(entity.Id) == policy.Id);
            Assert.Contains(policy.Id.ToString(), sensor.ToEntity().Policies);
        }

        [Fact]
        public async Task NoOtherRegistration_GetsTheAlert()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            await RegisterAsync(host, "large/plain-file", FileType, 0);
            await RegisterAsync(host, "large/integer", IntegerType, 4);
            await RegisterAsync(host, "large/without-status-change", FileType, 2);

            Assert.Empty(Sensor("large/plain-file").Policies);
            Assert.Empty(Sensor("large/integer").Policies);
            Assert.Empty(Sensor("large/without-status-change").Policies);
        }

        [Fact]
        public async Task ASensorThatExists_GetsNothingFromALaterRegistrationWithTheFlag()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            await RegisterAsync(host, "large/older", FileType, 0);
            await RegisterAsync(host, "large/older", FileType, 4);

            Assert.Empty(Sensor("large/older").Policies);
        }

        [Fact]
        public async Task TheFlag_TogetherWithTheSuppressionFlags_StillAddsTheAlert()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            await RegisterAsync(host, "large/all-flags", FileType, 1 | 2 | 4);

            Assert.Single(Sensor("large/all-flags").Policies);
        }


        // The alert list of a registration reconciles the collector's own alerts; the default one is not the collector's.
        [Fact]
        public async Task TheAlertListOfARegistration_NeverRemovesTheDefaultAlert()
        {
            const string collectorAlert = "{\"Template\":\"collector alert\",\"Conditions\":[{\"Operation\":23,\"Property\":0,\"Target\":{\"Type\":0,\"Value\":\"\"}}]}";

            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            // The creating registration with an empty list (as the native default-sensor prototypes send).
            await RegisterAsync(host, "large/listed", FileType, 4, "\"Alerts\":[]");
            Assert.Equal([SensorPolicyCollection.LargeFileAlertTemplate], Templates("large/listed"));

            // A later registration with one alert of the collector's: added beside it.
            await RegisterAsync(host, "large/listed", FileType, 4, $"\"Alerts\":[{collectorAlert}]");
            Assert.Equal([SensorPolicyCollection.LargeFileAlertTemplate, "collector alert"], Templates("large/listed"));

            // And without it again: the collector's alert goes, the default stays.
            await RegisterAsync(host, "large/listed", FileType, 4, "\"Alerts\":[]");
            Assert.Equal([SensorPolicyCollection.LargeFileAlertTemplate], Templates("large/listed"));
        }


        private string[] Templates(string path) => Sensor(path).Policies.Select(p => p.Template).Order(StringComparer.Ordinal).ToArray();

        private BaseSensorModel Sensor(string path)
        {
            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, path, out var sensor), path);
            return sensor;
        }

        private async Task RegisterAsync(SensorApiTestHost host, string path, int type, long defaultAlerts, string members = null)
        {
            var extra = members is null ? string.Empty : $",{members}";

            using var deadline = new CancellationTokenSource(Patience);
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(host.HttpBase, "commands"))
            {
                Content = new StringContent($"[{{\"Type\":0,\"Path\":\"{path}\",\"SensorType\":{type},\"DefaultAlertsOptions\":{defaultAlerts}{extra}}}]",
                    Encoding.UTF8, "application/json"),
            };

            request.Headers.Add("ClientName", "large-file-default-alert-tests");
            request.Headers.Add("Key", _key);

            using var response = await http.SendAsync(request, deadline.Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
