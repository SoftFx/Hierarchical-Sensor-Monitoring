using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Controllers;
using HSMServer.Controllers.DataTables;
using HSMServer.Core.Model.Policies;
using HSMServer.DTOs.Sensors;
using HSMServer.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.History;
using HSMServer.Model.Model.History;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.TreeViewModels;
using HSMServer.Model.UserTreeShallowCopy;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Controllers
{
    // Every HomeController action that takes a sensor, node or folder id from the client resolves it
    // through UserTreeAccess: a user with no role on the product (or folder) gets the answer of an unknown
    // id and changes nothing; a member of the product and an admin are served. An action that CHANGES what
    // it addresses (a row marked Manages) serves the product's Manager and the admin; a Viewer of the
    // product gets the answer of an unknown id there too. The table below is one row per action and kind
    // of id.
    public class HomeControllerAccessTests
    {
        private delegate Task<object> Call(HomeController home, WebAccessFixture tree, Guid id);

        private sealed record Case(Func<WebAccessFixture, Guid> Target, Call Call, bool Manages = false);

        private const bool Manages = true;

        private static Guid Sensor(WebAccessFixture tree) => tree.Cpu.Id;

        private static Guid FileSensor(WebAccessFixture tree) => tree.Report.Id;

        private static Guid Node(WebAccessFixture tree) => tree.Own.Id;

        private static Guid Folder(WebAccessFixture tree) => tree.Folder.Id;

        private static Task<object> Sync(object result) => Task.FromResult(result);

        private static readonly Dictionary<string, Case> Cases = new()
        {
            // ---- a sensor id
            ["SelectNode(sensor)"] = new(Sensor, async (home, _, id) => await home.SelectNode(id.ToString())),
            ["Index(sensor) opens its parents"] = new(Sensor, (home, _, id) =>
            {
                home.CurrentUser.Tree.ClearOpenedNodes();
                home.Index(id);
                return Sync(home.CurrentUser.Tree.OpenedNodes.Count());
            }),
            ["UpdateSelectedNode(sensor)"] = new(Sensor, (home, _, id) => Sync(home.UpdateSelectedNode(id.ToString()))),
            ["GetGeneralInfo(sensor)"] = new(Sensor, (home, _, id) => Sync(home.GetGeneralInfo(id.ToString()))),
            ["GetAlertIcons(sensor)"] = new(Sensor, (home, _, id) => Sync(home.GetAlertIcons(id.ToString()))),
            ["GetSensorInfo"] = new(Sensor, (home, _, id) => Sync(home.GetSensorInfo(id.ToString()))),
            ["GetNodePath(sensor)"] = new(Sensor, (home, _, id) => Sync(home.GetNodePath(id.ToString()))),
            ["RefreshHistoryInfo(sensor)"] = new(Sensor, (home, _, id) => Sync(home.RefreshHistoryInfo(id))),
            ["GetSensorEditModal"] = new(Sensor, (home, _, id) => Sync(home.GetSensorEditModal(id)), Manages),
            ["GetTestToastMessage"] = new(Sensor, (home, _, id) => Sync(home.GetTestToastMessage(
                new AlertMessageViewModel { EntityId = id, Comment = "test", Property = AlertProperty.Value, Operation = PolicyOperation.GreaterThan, Target = "1" }))),
            ["AddDataPolicy(sensor)"] = new(Sensor, (home, _, id) => Sync(AlertBlock(home.AddDataPolicy((byte)SensorType.Integer, id)))),
            ["MuteSensors(sensor) modal"] = new(Sensor, (home, _, id) => Sync(MutedPaths(home.MuteSensors([id.ToString()]))), Manages),
            ["RemoveNode(sensor)"] = new(Sensor, async (home, _, id) => await home.RemoveNode([id.ToString()])),
            ["EditAlerts(sensor)"] = new(Sensor, async (home, _, id) => await home.EditAlerts(new EditAlertsViewModel { NodeIds = id.ToString() })),
            ["UpdateSensorInfo(sensor), invalid form"] = new(Sensor, async (home, _, id) =>
            {
                home.ModelState.AddModelError("Description", "invalid");
                return await home.UpdateSensorInfo(new SensorInfoViewModel { EncodedId = id.ToString() });
            }, Manages),
            ["SetMutedStateToSensorFromModal(sensor)"] = new(Sensor, async (home, _, id) =>
            {
                await home.SetMutedStateToSensorFromModal(new IgnoreNotificationsViewModel { Ids = [id.ToString()], IgnorePeriod = new TimeIntervalViewModel() });
                return null;
            }, Manages),
            ["RemoveMutedStateToSensor(sensor)"] = new(Sensor, async (home, _, id) =>
            {
                await home.RemoveMutedStateToSensor([id.ToString()]);
                return null;
            }, Manages),
            ["ClearHistoryNode(sensor)"] = new(Sensor, async (home, _, id) =>
            {
                await home.ClearHistoryNode(id.ToString());
                return null;
            }, Manages),
            ["EnableGrafana(sensor)"] = new(Sensor, async (home, _, id) =>
            {
                await home.EnableGrafana(id.ToString());
                return null;
            }, Manages),
            ["DisableGrafana(sensor)"] = new(Sensor, async (home, _, id) =>
            {
                await home.DisableGrafana(id.ToString());
                return null;
            }, Manages),
            ["UpdateSensorDisplayUnit"] = new(Sensor, async (home, _, id) => await home.UpdateSensorDisplayUnit(id.ToString(), RateDisplayUnit.PerMinute), Manages),
            ["UpdateSensorTableSettings"] = new(Sensor, async (home, _, id) =>
                await home.UpdateSensorTableSettings(id, new TableSettingsUpdateDto { IsHideEnabled = true, MaxCommentHideSize = 10 }), Manages),
            ["UpdateSensorStatus"] = new(Sensor, async (home, tree, id) => await home.UpdateSensorStatus(new EditSensorStatusViewModal
            {
                SensorId = id,
                // The form's own product field is not trusted: an outsider posts a product they DO have a key in.
                RootProductId = tree.Foreign.Id,
                Path = "own/cpu",
                NewStatus = HSMServer.Model.TreeViewModel.SensorStatus.Error,
                Comment = "set by hand",
            }), Manages),

            // ---- a file sensor id
            ["GetFile"] = new(FileSensor, async (home, _, id) => await home.GetFile(id.ToString())),
            ["GetFileStream"] = new(FileSensor, async (home, _, id) => await home.GetFileStream(id.ToString())),
            ["GetFileInfo"] = new(FileSensor, (home, _, id) => Sync(home.GetFileInfo(id.ToString()))),

            // ---- a node id
            ["SelectNode(node)"] = new(Node, async (home, _, id) => await home.SelectNode(id.ToString())),
            ["GetNode"] = new(Node, (home, _, id) =>
            {
                home.RefreshTree(new SearchPattern());

                return Sync(home.GetNode(id.ToString()) is PartialViewResult { Model: NodeShallowModel node } ? $"node:{node.Id}" : "nothing");
            }),
            ["UpdateSelectedNode(node)"] = new(Node, (home, _, id) => Sync(home.UpdateSelectedNode(id.ToString()))),
            ["GetGeneralInfo(node)"] = new(Node, (home, _, id) => Sync(home.GetGeneralInfo(id.ToString()))),
            ["GetAlertIcons(node)"] = new(Node, (home, _, id) => Sync(home.GetAlertIcons(id.ToString()))),
            ["GetProductInfo"] = new(Node, (home, _, id) => Sync(home.GetProductInfo(id.ToString()))),
            ["GetNodePath(node)"] = new(Node, (home, _, id) => Sync(home.GetNodePath(id.ToString()))),
            ["RefreshHistoryInfo(node)"] = new(Node, (home, _, id) => Sync(home.RefreshHistoryInfo(id))),
            ["ExportNode"] = new(Node, (home, _, id) => Sync(home.ExportNode(id.ToString()))),
            ["SaveHistoryInfo"] = new(Node, (home, _, id) => Sync(home.SaveHistoryInfo(id))),
            ["MuteSensors(node) modal"] = new(Node, (home, _, id) => Sync(MutedPaths(home.MuteSensors([id.ToString()]))), Manages),
            ["RemoveNode(node)"] = new(Node, async (home, _, id) => await home.RemoveNode([id.ToString()])),
            ["EditAlerts(node)"] = new(Node, async (home, _, id) => await home.EditAlerts(new EditAlertsViewModel { NodeIds = id.ToString() })),
            ["UpdateProductInfo(node), invalid form"] = new(Node, async (home, _, id) =>
            {
                home.ModelState.AddModelError("Description", "invalid");
                return await home.UpdateProductInfo(new ProductInfoViewModel { EncodedId = id.ToString() });
            }, Manages),
            ["SetMutedStateToSensorFromModal(node)"] = new(Node, async (home, _, id) =>
            {
                await home.SetMutedStateToSensorFromModal(new IgnoreNotificationsViewModel { Ids = [id.ToString()], IgnorePeriod = new TimeIntervalViewModel() });
                return null;
            }, Manages),
            ["RemoveMutedStateToSensor(node)"] = new(Node, async (home, _, id) =>
            {
                await home.RemoveMutedStateToSensor([id.ToString()]);
                return null;
            }, Manages),
            ["ClearHistoryNode(node)"] = new(Node, async (home, _, id) =>
            {
                await home.ClearHistoryNode(id.ToString());
                return null;
            }, Manages),
            ["EnableGrafana(node)"] = new(Node, async (home, _, id) =>
            {
                await home.EnableGrafana(id.ToString());
                return null;
            }, Manages),

            // ---- a folder id
            ["SelectNode(folder)"] = new(Folder, async (home, _, id) => await home.SelectNode(id.ToString())),
            ["GetGeneralInfo(folder)"] = new(Folder, (home, _, id) => Sync(home.GetGeneralInfo(id.ToString()))),
            ["GetFolderInfo"] = new(Folder, (home, _, id) => Sync(home.GetFolderInfo(id.ToString()))),
            ["MuteSensors(folder) modal"] = new(Folder, (home, _, id) => Sync(MutedPaths(home.MuteSensors([id.ToString()]))), Manages),
            ["RemoveNode(folder)"] = new(Folder, async (home, _, id) => await home.RemoveNode([id.ToString()])),
            ["AddDataPolicy(template, folder)"] = new(Folder, (home, _, id) => Sync(AlertBlock(home.AddDataPolicy((byte)SensorType.Integer, Guid.Empty, id)))),
            ["EnableGrafana(folder)"] = new(Folder, async (home, _, id) =>
            {
                await home.EnableGrafana(id.ToString());
                return null;
            }, Manages),
        };

        // The rows whose action writes to the cache when it is served: being served means the write happened.
        private static readonly string[] WritingActions =
        [
            "SetMutedStateToSensorFromModal", "RemoveMutedStateToSensor", "ClearHistoryNode", "EnableGrafana", "DisableGrafana",
            "UpdateSensorDisplayUnit", "UpdateSensorTableSettings", "UpdateSensorStatus",
            "RemoveNode(sensor)", "RemoveNode(node)", "EditAlerts(sensor)", "EditAlerts(node)",
        ];

        private static bool Writes(string action) => WritingActions.Any(action.StartsWith);

        public static TheoryData<string> Actions => Names(_ => true);

        /// <summary>The actions that only show what they address: any role on the product serves them.</summary>
        public static TheoryData<string> ViewerActions => Names(c => !c.Manages);

        /// <summary>The actions that change what they address: the Manager role serves them.</summary>
        public static TheoryData<string> ManagerActions => Names(c => c.Manages);

        private static TheoryData<string> Names(Func<Case, bool> filter)
        {
            var data = new TheoryData<string>();

            foreach (var (name, @case) in Cases)
                if (filter(@case))
                    data.Add(name);

            return data;
        }


        [Theory]
        [MemberData(nameof(Actions))]
        public async Task Outsider_GetsTheAnswerOfAnUnknownId_AndChangesNothing(string action)
        {
            var tree = new WebAccessFixture();
            var target = Cases[action].Target(tree);

            var refused = await Ask(tree, tree.Outsider, action, target);
            var unknown = await Ask(tree, tree.Outsider, action, Guid.NewGuid());

            Assert.Equal(unknown.Answer, refused.Answer);
            Assert.Equal(0, refused.Writes);

            // The same answer, and an answer: not the same server error twice.
            Assert.DoesNotContain("throws:", refused.Answer);
        }

        [Theory]
        [MemberData(nameof(ViewerActions))]
        public async Task Member_IsServed(string action)
        {
            var tree = new WebAccessFixture();
            var target = Cases[action].Target(tree);

            var unknown = await Ask(tree, tree.Member, action, Guid.NewGuid());
            var served = await Ask(tree, tree.Member, action, target);

            Assert.NotEqual(unknown, served);
            Assert.DoesNotContain("throws:", served.Answer);
        }

        [Theory]
        [MemberData(nameof(ManagerActions))]
        public async Task Member_WhoIsNotManager_GetsTheAnswerOfAnUnknownId_AndChangesNothing(string action)
        {
            // The member sees the product (Viewer): that is not enough to change it.
            var tree = new WebAccessFixture();
            var target = Cases[action].Target(tree);

            var refused = await Ask(tree, tree.Member, action, target);
            var unknown = await Ask(tree, tree.Member, action, Guid.NewGuid());

            Assert.Equal(unknown.Answer, refused.Answer);
            Assert.Equal(0, refused.Writes);
        }

        [Theory]
        [MemberData(nameof(Actions))]
        public async Task Manager_IsServed(string action)
        {
            var tree = new WebAccessFixture();
            var target = Cases[action].Target(tree);

            var unknown = await Ask(tree, tree.Manager, action, Guid.NewGuid());
            var served = await Ask(tree, tree.Manager, action, target);

            Assert.NotEqual(unknown, served);
            Assert.DoesNotContain("throws:", served.Answer);
            Assert.Equal(Writes(action), served.Writes > 0);
        }

        [Theory]
        [MemberData(nameof(Actions))]
        public async Task Admin_IsServed(string action)
        {
            var tree = new WebAccessFixture();
            var target = Cases[action].Target(tree);

            var unknown = await Ask(tree, tree.Admin, action, Guid.NewGuid());
            var served = await Ask(tree, tree.Admin, action, target);

            Assert.NotEqual(unknown, served);
            Assert.DoesNotContain("throws:", served.Answer);
            Assert.Equal(Writes(action), served.Writes > 0);
        }


        [Theory]
        [InlineData(nameof(HomeController.Compact))]
        [InlineData(nameof(HomeController.Export))]
        [InlineData(nameof(HomeController.Unseal))]
        public void DatabaseMaintenance_IsAdminOnly(string action)
        {
            var attributes = typeof(HomeController).GetMethod(action)!.GetCustomAttributes(typeof(HSMServer.Attributes.AuthorizeIsAdminAttribute), inherit: false);

            Assert.NotEmpty(attributes);
        }


        // ---- per-user state: what SelectNode connected is checked again by its readers

        [Fact]
        public async Task SelectNode_Refused_ConnectsNothing_AndOpensNothing()
        {
            var tree = new WebAccessFixture();
            var home = tree.Home(tree.Outsider);

            foreach (var id in new[] { tree.Cpu.Id, tree.Own.Id, tree.Folder.Id })
                await home.SelectNode(id.ToString());

            Assert.Null(tree.Outsider.SelectedNode.Id);
            Assert.Null(tree.Outsider.History.Table);
            Assert.Null(tree.Outsider.Journal.ConnectedId);
            Assert.Empty(tree.Outsider.Tree.OpenedNodes);
            Assert.Empty(tree.Journal.Asked);
        }

        [Fact]
        public async Task ChildrenPages_AreServedToAMember_AndStopWhenTheRoleIsGone()
        {
            var tree = new WebAccessFixture();
            var home = tree.Home(tree.Member);
            var page = new ChildrenPageRequest("Sensors", 0, 10);

            await home.SelectNode(tree.Own.Id.ToString());

            Assert.IsType<PartialViewResult>(home.GetGrid(page));
            Assert.IsType<PartialViewResult>(home.GetList(page));

            // The state outlives the role: the selection is checked again on every page.
            tree.Member.ProductsRoles.Clear();

            Assert.IsType<EmptyResult>(home.GetGrid(page));
            Assert.IsType<EmptyResult>(home.GetList(page));
        }

        [Fact]
        public async Task Folder_ShowsAMemberOnlyTheProductsTheySee()
        {
            var tree = new WebAccessFixture();
            var home = tree.Home(tree.Member);

            await home.SelectNode(tree.Folder.Id.ToString());

            var grid = Assert.IsType<PartialViewResult>(home.GetGrid(new ChildrenPageRequest("Products", 0, 10)));
            var products = Assert.IsAssignableFrom<INodeChildrenViewModel>(grid.Model).VisibleItems.Select(p => p.Id).ToList();

            Assert.Equal([tree.Own.Id], products);

            // ... and the journal of the folder is loaded for those products only.
            Assert.Contains(tree.Own.Id, tree.Journal.Asked);
            Assert.Contains(tree.Cpu.Id, tree.Journal.Asked);
            Assert.DoesNotContain(tree.Sibling.Id, tree.Journal.Asked);
            Assert.DoesNotContain(tree.SiblingCpu.Id, tree.Journal.Asked);
        }

        [Fact]
        public async Task Folder_GrafanaSwitch_TouchesOnlyTheProductsTheUserManages()
        {
            // The folder's Manager manages one of its two products.
            var tree = new WebAccessFixture();

            await tree.Home(tree.Manager).EnableGrafana(tree.Folder.Id.ToString());

            var touched = SensorUpdates(tree);

            Assert.Contains(tree.Cpu.Id, touched);
            Assert.Contains(tree.ServiceAlive.Id, touched);
            Assert.DoesNotContain(tree.SiblingCpu.Id, touched);
        }

        [Fact]
        public async Task Folder_GrafanaSwitch_NeedsTheFolderManagerRole_AndTheProductManagerRole()
        {
            var tree = new WebAccessFixture();

            // Manager of a product in the folder, no role on the folder itself.
            var productManager = new User("product manager");
            productManager.ProductsRoles.Add((tree.Own.Id, ProductRoleEnum.ProductManager));
            tree.Users.Setup(u => u[productManager.Id]).Returns(productManager);

            // Manager of the folder who is a Viewer of its products.
            var folderManager = new User("folder manager");
            folderManager.FoldersRoles.Add(tree.Folder.Id, ProductRoleEnum.ProductManager);
            folderManager.ProductsRoles.Add((tree.Own.Id, ProductRoleEnum.ProductViewer));
            folderManager.ProductsRoles.Add((tree.Sibling.Id, ProductRoleEnum.ProductViewer));
            tree.Users.Setup(u => u[folderManager.Id]).Returns(folderManager);

            await tree.Home(productManager).EnableGrafana(tree.Folder.Id.ToString());
            await tree.Home(folderManager).EnableGrafana(tree.Folder.Id.ToString());

            Assert.Empty(SensorUpdates(tree));

            // The product itself is the product Manager's to switch.
            await tree.Home(productManager).EnableGrafana(tree.Own.Id.ToString());

            Assert.Contains(tree.Cpu.Id, SensorUpdates(tree));
        }

        [Fact]
        public async Task NestedNode_IsManagedThroughItsRootProduct()
        {
            // Roles are held per root product: a role entry on a nested node's own id counts for nothing.
            var tree = new WebAccessFixture();
            var module = tree.ServiceAlive.Parent;

            var onNodeOnly = new User("role on the node id");
            onNodeOnly.ProductsRoles.Add((module.Id, ProductRoleEnum.ProductManager));
            onNodeOnly.ProductsRoles.Add((tree.Own.Id, ProductRoleEnum.ProductViewer));
            tree.Users.Setup(u => u[onNodeOnly.Id]).Returns(onNodeOnly);

            await tree.Home(onNodeOnly).EnableGrafana(module.Id.ToString());
            await tree.Home(onNodeOnly).ClearHistoryNode(module.Id.ToString());

            Assert.Equal(0, tree.Writes());

            await tree.Home(tree.Manager).EnableGrafana(module.Id.ToString());

            Assert.Equal([tree.ServiceAlive.Id], SensorUpdates(tree));
        }

        [Fact]
        public async Task ProductSettings_AreTheManagersToChange()
        {
            var tree = new WebAccessFixture();

            ProductInfoViewModel Form() => new(tree.Tree.Nodes[tree.Own.Id]) { Description = "changed" };

            foreach (var refused in new[] { tree.Outsider, tree.Member })
                Assert.IsType<EmptyResult>(await tree.Home(refused).UpdateProductInfo(Form()));

            Assert.Equal(0, tree.Writes());

            Assert.IsType<PartialViewResult>(await tree.Home(tree.Manager).UpdateProductInfo(Form()));
            Assert.IsType<PartialViewResult>(await tree.Home(tree.Admin).UpdateProductInfo(Form()));
            Assert.Equal(2, tree.Writes());
        }

        [Fact]
        public async Task JournalFeed_IsServedToAMember_AndStopsWhenTheRoleIsGone()
        {
            var tree = new WebAccessFixture();
            var feed = tree.JournalFeed(tree.Member);

            Assert.Equal(0, Rows(feed.GetPage(Parameters())));

            await tree.Home(tree.Member).SelectNode(tree.Cpu.Id.ToString());

            Assert.Equal(1, Rows(feed.GetPage(Parameters())));

            tree.Member.ProductsRoles.Clear();

            Assert.Equal(0, Rows(feed.GetPage(Parameters())));
        }

        [Fact]
        public async Task JournalFeed_SendsPlainCellsEncoded()
        {
            // The per-node journal table inserts every cell as HTML; an initiator is a user or key name.
            var tree = new WebAccessFixture();

            tree.Journal.Initiator = "<img src=x onerror=alert(1)>";
            await tree.Home(tree.Admin).SelectNode(tree.Cpu.Id.ToString());

            var parameters = new DataTableParameters(
                Columns: [new DataTableColumn(0, nameof(ColumnName.Initiator), false, false, new Search(false, string.Empty))],
                Draw: 1, Length: 50, Order: [], Search: new Search(false, string.Empty), Start: 0, NeedSearchPath: false);

            var cell = ((DataTableResultSet)tree.JournalFeed(tree.Admin).GetPage(parameters).Value).Data.Single().Single();

            Assert.Equal("&lt;img src=x onerror=alert(1)&gt;", cell);
        }

        [Fact]
        public async Task RecentFiles_AreTheFilesOfTheSensorAsked_AndStopWhenTheRoleIsGone()
        {
            var tree = new WebAccessFixture();
            var home = tree.Home(tree.Member);

            // Another sensor is selected (another browser tab): the request names the file sensor and gets
            // the file sensor's files.
            await home.SelectNode(tree.Cpu.Id.ToString());

            var recent = Assert.IsType<PartialViewResult>(await home.GetRecentFilesView(tree.Report.Id.ToString()));
            var table = Assert.IsType<HistoryTableViewModel>(recent.Model);

            Assert.Equal(tree.Report.Id.ToString(), table.SensorId);

            // One of the listed files, by its receiving time, is downloadable.
            var ticks = table.Pages[0].Cast<FileValue>().First().ReceivingTime.Ticks;

            Assert.IsType<FileContentResult>(await home.GetFile(tree.Report.Id.ToString(), ticks));

            // A sensor that is not a file sensor has no files.
            Assert.IsType<EmptyResult>(await home.GetRecentFilesView(tree.Cpu.Id.ToString()));

            tree.Member.ProductsRoles.Clear();

            Assert.IsType<EmptyResult>(await home.GetRecentFilesView(tree.Report.Id.ToString()));
            Assert.IsType<NotFoundResult>(await home.GetFile(tree.Report.Id.ToString(), ticks));
        }

        [Fact]
        public async Task Folder_FollowsARoleTakenFromOneOfItsProducts()
        {
            var tree = new WebAccessFixture();
            var user = new User("both");

            user.ProductsRoles.Add((tree.Own.Id, ProductRoleEnum.ProductViewer));
            user.ProductsRoles.Add((tree.Sibling.Id, ProductRoleEnum.ProductViewer));
            tree.Users.Setup(u => u[user.Id]).Returns(user);

            var home = tree.Home(user);
            var page = new ChildrenPageRequest("Products", 0, 10);

            await home.SelectNode(tree.Folder.Id.ToString());

            Assert.Equal(2, Products(home.GetGrid(page)).Count);
            Assert.Equal(2, ((FolderInfoViewModel)((PartialViewResult)home.GetFolderInfo(tree.Folder.Id.ToString())).Model).TotalProducts);

            var rowsWithBoth = Rows(tree.JournalFeed(user).GetPage(Parameters()));

            // The folder stays selected and visible (through the other product); what it shows follows.
            user.ProductsRoles.RemoveAll(role => role.Item1 == tree.Sibling.Id);

            Assert.Equal([tree.Own.Id], Products(home.GetGrid(page)));
            Assert.Equal([tree.Own.Id], Products(home.GetList(page)));
            Assert.Equal(1, ((FolderInfoViewModel)((PartialViewResult)home.GetFolderInfo(tree.Folder.Id.ToString())).Model).TotalProducts);

            var feed = (DataTableResultSet)tree.JournalFeed(user).GetPage(Parameters()).Value;

            // One record per loaded id (the fake journal): the sibling product and its sensor are gone
            // from the rows and from both counts.
            Assert.Equal(rowsWithBoth - 2, feed.Data.Length);
            Assert.Equal(rowsWithBoth - 2, feed.RecordsTotal);
            Assert.Equal(rowsWithBoth - 2, feed.RecordsFiltered);
        }

        [Fact]
        public void Folder_PanelCountsOnlyTheProductsTheUserSees()
        {
            var tree = new WebAccessFixture();

            var member = (FolderInfoViewModel)((PartialViewResult)tree.Home(tree.Member).GetFolderInfo(tree.Folder.Id.ToString())).Model;
            var admin = (FolderInfoViewModel)((PartialViewResult)tree.Home(tree.Admin).GetFolderInfo(tree.Folder.Id.ToString())).Model;

            Assert.Equal(1, member.TotalProducts);
            Assert.Equal(2, admin.TotalProducts);
        }

        [Fact]
        public async Task FolderSettings_AreTheFolderManagersToChange()
        {
            // A folder's settings are inherited by every product in it: seeing the folder through one of
            // its products is not enough to change them.
            var tree = new WebAccessFixture();
            var manager = new User("folder manager");

            manager.FoldersRoles.Add(tree.Folder.Id, ProductRoleEnum.ProductManager);
            tree.Users.Setup(u => u[manager.Id]).Returns(manager);

            FolderInfoViewModel Form() => new() { EncodedId = tree.Folder.Id.ToString(), Description = "changed" };

            foreach (var refused in new[] { tree.Outsider, tree.Member })
                Assert.IsType<EmptyResult>(await tree.Home(refused).UpdateFolderInfo(Form()));

            Assert.Equal(0, tree.Writes());

            Assert.IsType<PartialViewResult>(await tree.Home(manager).UpdateFolderInfo(Form()));
            Assert.IsType<PartialViewResult>(await tree.Home(tree.Admin).UpdateFolderInfo(Form()));
            Assert.Equal(2, tree.Writes());
        }


        private static async Task<Observation> Ask(WebAccessFixture tree, User user, string action, Guid id)
        {
            var home = tree.Home(user);
            var writesBefore = tree.Writes();
            string answer;

            try
            {
                answer = WebAccessFixture.Describe(await Cases[action].Call(home, tree, id));
            }
            catch (Exception ex)
            {
                answer = $"throws:{ex.GetType().Name}";
            }

            // The asked id itself is not part of the answer's meaning.
            return new Observation(answer.Replace(id.ToString(), "<id>"), tree.Writes() - writesBefore);
        }

        // What the mute modal names: the paths of the items it was opened for.
        private static object MutedPaths(IActionResult result) =>
            string.Join("|", ((IgnoreNotificationsViewModel)((PartialViewResult)result).Model).Paths.Select(p => p ?? "-"));

        // What a new alert block carries from its owner: the sensor it belongs to and the chats offered.
        private static object AlertBlock(IActionResult result)
        {
            var alert = (DataAlertViewModelBase)((PartialViewResult)result).Model;

            return $"{alert.EntityId}:{alert.Actions.Sum(a => a.AvailableChats.Count)}";
        }

        private static List<Guid> SensorUpdates(WebAccessFixture tree) =>
            tree.Cache.Invocations
                .Where(i => i.Method.Name == nameof(HSMServer.Core.Cache.ITreeValuesCache.UpdateSensorAsync))
                .Select(i => ((HSMServer.Core.Cache.UpdateEntities.SensorUpdate)i.Arguments[0]).Id)
                .ToList();

        private static List<Guid> Products(IActionResult grid) =>
            Assert.IsAssignableFrom<INodeChildrenViewModel>(Assert.IsType<PartialViewResult>(grid).Model).VisibleItems.Select(p => p.Id).ToList();

        private static DataTableParameters Parameters() => new(
            Columns: [new DataTableColumn(0, nameof(ColumnName.Record), false, false, new Search(false, string.Empty))],
            Draw: 1, Length: 50, Order: [], Search: new Search(false, string.Empty), Start: 0, NeedSearchPath: false);

        private static int Rows(JsonResult result) => ((DataTableResultSet)result.Value).Data.Length;


        private sealed record Observation(string Answer, int Writes);
    }
}
