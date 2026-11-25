using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Model.DataAlerts;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // The alerts import of the tree ("Alerts" > "Import" on a node or folder) writes the imported alerts
    // to the sensors its file names, through the policy administration service.
    public class AlertsControllerImportTests
    {
        [Fact]
        public async Task Import_ToANode_WritesTheAlertToTheNamedSensor()
        {
            var tree = new WebAccessFixture();

            var answer = await tree.Alerts(tree.Admin).ImportAlerts(Import(tree.Own.Id, File(sensors: ["cpu"])));

            Assert.Equal(string.Empty, answer);

            var written = Assert.Single(ImportedPolicies(tree));

            Assert.Equal(tree.Cpu.Id, written.Id);
            Assert.Equal("imported", Assert.Single(written.Policies).Template);
        }

        [Fact]
        public async Task Import_ToANestedNode_ResolvesTheSensorBelowIt()
        {
            // The paths of a file are relative to the node the import was started on.
            var tree = new WebAccessFixture();

            var answer = await tree.Alerts(tree.Admin).ImportAlerts(Import(tree.ServiceAlive.Parent.Id, File(sensors: ["Service alive"])));

            Assert.Equal(string.Empty, answer);
            Assert.Equal([tree.ServiceAlive.Id], ImportedPolicies(tree).Select(u => u.Id));
        }

        [Fact]
        public async Task Import_ByProductName_WritesToEachNamedProduct()
        {
            // A file exported from a folder names its products; the node the import was started on is not used.
            var tree = new WebAccessFixture();

            var answer = await tree.Alerts(tree.Admin).ImportAlerts(
                Import(tree.Folder.Id, File(sensors: ["cpu"], products: ["own", "sibling", "no such product"])));

            Assert.Equal(string.Empty, answer);
            Assert.Equal(new[] { tree.Cpu.Id, tree.SiblingCpu.Id }.Order(), ImportedPolicies(tree).Select(u => u.Id).Order());
        }

        [Fact]
        public async Task Import_ReportsASensorTheFileNamesButTheNodeDoesNotHave()
        {
            var tree = new WebAccessFixture();

            var answer = await tree.Alerts(tree.Admin).ImportAlerts(Import(tree.Own.Id, File(sensors: ["cpu", "no such sensor"])));

            Assert.Contains("Sensor by path not found", answer);
            Assert.Contains("no such sensor", answer);
            Assert.Equal([tree.Cpu.Id], ImportedPolicies(tree).Select(u => u.Id));
        }


        internal static AlertImportViewModel Import(Guid nodeId, string file) => new() { NodeId = nodeId, FileContent = file };

        /// <summary>An exported alerts file with one alert for the given relative sensor paths.</summary>
        internal static string File(string[] sensors, string[] products = null) =>
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    Products = products,
                    Sensors = sensors,
                    Conditions = new[] { new { Property = "Value", Operation = "GreaterThan", Target = "10" } },
                    Template = "imported",
                    Icon = "!",
                    Chats = new[] { "#empty" },
                },
            });

        /// <summary>The policy list writes the cache has received.</summary>
        internal static List<SensorUpdate> ImportedPolicies(WebAccessFixture tree) =>
            tree.Cache.Invocations
                .Where(i => i.Method.Name == nameof(ITreeValuesCache.UpdateSensorAsync))
                .Select(i => (SensorUpdate)i.Arguments[0])
                .Where(u => u.Policies is not null)
                .ToList();
    }
}
