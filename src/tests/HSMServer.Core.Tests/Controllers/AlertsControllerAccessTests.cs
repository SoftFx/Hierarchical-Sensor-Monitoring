using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using static HSMServer.Core.Tests.Controllers.AlertsControllerImportTests;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Controllers
{
    // The tree offers the alerts export and import of a node to the Managers of its product, and those of
    // a folder to the folder's Managers. The actions follow: Manager of the root product (or of the
    // folder), or admin; everyone else gets the answer of an unknown id, and an import leaves no trace of
    // what its file named.
    public class AlertsControllerAccessTests
    {
        // ---- export of a node

        [Fact]
        public void Export_OfANode_IsServedToItsManager_AndToAnAdmin()
        {
            var tree = new WebAccessFixture();

            foreach (var user in new[] { tree.Manager, tree.Admin })
            {
                var file = Assert.IsType<FileContentResult>(tree.Alerts(user).ExportAlerts(tree.Own.Id));

                Assert.Equal("own-alerts.json", file.FileDownloadName);
            }
        }

        [Fact]
        public void Export_OfANode_IsAnsweredLikeAnUnknownId_ToEveryoneElse()
        {
            var tree = new WebAccessFixture();

            foreach (var user in new[] { tree.Outsider, tree.Member })
            {
                var alerts = tree.Alerts(user);

                var refused = WebAccessFixture.Describe(alerts.ExportAlerts(tree.Own.Id));
                var unknown = WebAccessFixture.Describe(alerts.ExportAlerts(Guid.NewGuid()));

                Assert.Equal("empty", refused);
                Assert.Equal(unknown, refused);
                Assert.False(alerts.Response.Headers.ContainsKey("Content-Disposition"));
            }
        }

        [Fact]
        public void Export_OfANestedNode_FollowsTheRootProduct()
        {
            var tree = new WebAccessFixture();
            var module = tree.ServiceAlive.Parent.Id;

            var onNodeOnly = new User("role on the node id");
            onNodeOnly.ProductsRoles.Add((module, ProductRoleEnum.ProductManager));
            onNodeOnly.ProductsRoles.Add((tree.Own.Id, ProductRoleEnum.ProductViewer));

            Assert.IsType<FileContentResult>(tree.Alerts(tree.Manager).ExportAlerts(module));
            Assert.IsType<EmptyResult>(tree.Alerts(onNodeOnly).ExportAlerts(module));
        }

        // ---- export of a folder

        [Fact]
        public void Export_OfAFolder_HoldsTheProductsItsManagerManages()
        {
            // The folder's Manager manages one of its two products; the admin all of them.
            var tree = new WebAccessFixture();

            AddAlert(tree.Cpu);
            AddAlert(tree.SiblingCpu);

            var manager = Text(Assert.IsType<FileContentResult>(tree.Alerts(tree.Manager).ExportFolderAlerts(tree.Folder.Id)));
            var admin = Text(Assert.IsType<FileContentResult>(tree.Alerts(tree.Admin).ExportFolderAlerts(tree.Folder.Id)));

            Assert.Contains("\"own\"", manager);
            Assert.DoesNotContain("\"sibling\"", manager);

            Assert.Contains("\"own\"", admin);
            Assert.Contains("\"sibling\"", admin);
        }

        [Fact]
        public void Export_OfAFolder_IsAnsweredLikeAnUnknownId_ToEveryoneElse()
        {
            var tree = new WebAccessFixture();

            // Manages a product of the folder, with no role on the folder.
            var productManager = new User("product manager");
            productManager.ProductsRoles.Add((tree.Own.Id, ProductRoleEnum.ProductManager));

            // Sees the folder through its role, but does not manage it.
            var folderViewer = new User("folder viewer");
            folderViewer.FoldersRoles.Add(tree.Folder.Id, ProductRoleEnum.ProductViewer);

            foreach (var user in new[] { tree.Outsider, tree.Member, productManager, folderViewer })
            {
                var alerts = tree.Alerts(user);

                var refused = WebAccessFixture.Describe(alerts.ExportFolderAlerts(tree.Folder.Id));
                var unknown = WebAccessFixture.Describe(alerts.ExportFolderAlerts(Guid.NewGuid()));

                Assert.Equal("empty", refused);
                Assert.Equal(unknown, refused);
            }
        }

        // ---- import

        [Fact]
        public async Task Import_OfANodeFile_OnAFolder_ReportsThatNothingWasPlaced()
        {
            // The tree offers the import on a folder to its Manager. A file exported from a node names no
            // products, and a folder is no node to import into: the answer says so instead of a success.
            var tree = new WebAccessFixture();

            var answer = await tree.Alerts(tree.Manager).ImportAlerts(Import(tree.Folder.Id, File(sensors: ["cpu"])));

            Assert.Contains(HSMServer.Controllers.AlertsController.ImportTargetNotFound, answer);
            Assert.Empty(ImportedPolicies(tree));
        }

        [Fact]
        public async Task Import_ToANode_IsWrittenForItsManager_AndForAnAdmin()
        {
            foreach (var pick in new Func<WebAccessFixture, User>[] { t => t.Manager, t => t.Admin })
            {
                var tree = new WebAccessFixture();

                var answer = await tree.Alerts(pick(tree)).ImportAlerts(Import(tree.Own.Id, File(sensors: ["cpu"])));

                Assert.Equal(string.Empty, answer);
                Assert.Equal([tree.Cpu.Id], ImportedPolicies(tree).Select(u => u.Id));
            }
        }

        [Fact]
        public async Task Import_ToANodeTheUserDoesNotManage_IsAnsweredLikeAnUnknownId_AndWritesNothing()
        {
            // The file names a sensor that exists and one that does not: neither leaves a trace in the answer.
            var file = File(sensors: ["cpu", "no such sensor"]);

            foreach (var pick in new Func<WebAccessFixture, User>[] { t => t.Outsider, t => t.Member })
            {
                var tree = new WebAccessFixture();
                var alerts = tree.Alerts(pick(tree));

                var refused = await alerts.ImportAlerts(Import(tree.Own.Id, file));
                var unknown = await alerts.ImportAlerts(Import(Guid.NewGuid(), file));

                Assert.Equal(unknown, refused);
                Assert.Contains(HSMServer.Controllers.AlertsController.ImportTargetNotFound, refused);
                Assert.DoesNotContain("no such sensor", refused);
                Assert.Empty(ImportedPolicies(tree));
            }
        }

        [Fact]
        public async Task Import_ByProductName_ReachesOnlyTheProductsTheUserManages()
        {
            // The names of a file are looked up over the whole server; a product the user does not manage
            // is passed over like a name no product has, whether its sensor exists or not.
            var tree = new WebAccessFixture();
            var file = File(sensors: ["cpu", "no such sensor"], products: ["own", "sibling", "foreign", "no such product"]);

            var answer = await tree.Alerts(tree.Manager).ImportAlerts(Import(tree.Folder.Id, file));

            Assert.Equal([tree.Cpu.Id], ImportedPolicies(tree).Select(u => u.Id));

            // The managed product's own missing sensor is reported; the other names are reported alike,
            // whether a product has them or not, and nothing is looked up in them.
            Assert.Contains("no such sensor", answer);
            Assert.Contains(tree.Own.Id.ToString(), answer);
            Assert.DoesNotContain(tree.Sibling.Id.ToString(), answer);
            Assert.DoesNotContain(tree.Foreign.Id.ToString(), answer);
            Assert.Contains(HSMServer.Controllers.AlertsController.ProductNotFound, answer);

            foreach (var name in new[] { "sibling", "foreign", "no such product" })
                Assert.Contains(name, answer);
        }

        [Fact]
        public async Task Import_ByProductName_ByAUserWhoManagesNoneOfThem_IsAnsweredLikeUnknownNames()
        {
            var tree = new WebAccessFixture();
            var alerts = tree.Alerts(tree.Outsider);

            var refused = await alerts.ImportAlerts(Import(tree.Foreign.Id, File(sensors: ["cpu", "no such sensor"], products: ["own", "sibling"])));
            var unknown = await alerts.ImportAlerts(Import(tree.Foreign.Id, File(sensors: ["cpu", "no such sensor"], products: ["no such 1", "no such 2"])));

            // The answer repeats the names of the file and nothing else.
            Assert.Equal(unknown.Replace("no such 1", "own").Replace("no such 2", "sibling"), refused);
            Assert.DoesNotContain("no such sensor", refused);
            Assert.Empty(ImportedPolicies(tree));
        }


        private static string Text(FileContentResult file) => Encoding.UTF8.GetString(file.FileContents);

        // One alert on a sensor, so that an export of its product has a row naming the product.
        private static void AddAlert(BaseSensorModel sensor)
        {
            var update = new PolicyUpdate
            {
                Id = Guid.NewGuid(),
                Template = "exported",
                Status = SensorStatus.Ok,
                Destination = new PolicyDestinationUpdate([], PolicyDestinationMode.Empty),
            };

            ((SensorPolicyCollection<IntegerValue, IntegerPolicy>)sensor.Policies)
                .TryUpdate([update], InitiatorInfo.AsUser("test"), out _);

            Assert.Single(sensor.Policies);
        }
    }
}
