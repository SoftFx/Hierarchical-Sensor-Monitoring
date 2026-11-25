using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMServer.Controllers;
using HSMServer.Model.History;
using HSMServer.Model.Model.History;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Controllers
{
    // History is read by sensor or node id from the client. The controller resolves every id through
    // UserTreeAccess, and the history table kept in the user's state is handed out only while its sensor
    // is visible: a user with no role on the product gets the answer of an unknown id; a member of the
    // product and an admin get the data.
    public class SensorHistoryControllerAccessTests
    {
        private delegate Task<object> Call(SensorHistoryController history, WebAccessFixture tree, Guid id);

        private sealed record Case(Func<WebAccessFixture, Guid> Target, Call Call);

        private static GetSensorHistoryRequest Request(Guid id) => new()
        {
            EncodedId = id.ToString(),
            From = DateTime.UtcNow.AddDays(-1),
            To = DateTime.UtcNow.AddDays(1),
            Count = -10,
            BarsCount = 100,
        };

        private static readonly Dictionary<string, Case> Cases = new()
        {
            ["ChartHistory"] = new(t => t.Cpu.Id, async (history, _, id) => await history.ChartHistory(Request(id))),
            ["ChartHistoryLatest"] = new(t => t.Cpu.Id, async (history, _, id) => await history.ChartHistoryLatest(Request(id))),
            ["GetSensorPlotInfo"] = new(t => t.Cpu.Id, (history, _, id) => Task.FromResult<object>(history.GetSensorPlotInfo(id))),
            ["GetBackgroundSensorInfo"] = new(t => t.Cpu.Id, (history, _, id) => Task.FromResult<object>(history.GetBackgroundSensorInfo(id))),
            ["GetServiceStatusHistory(service sensor)"] = new(t => t.ServiceAlive.Id, async (history, _, id) => await history.GetServiceStatusHistory(Request(id))),
            ["GetServiceStatusHistory(its background)"] = new(t => t.Cpu.Id, async (history, _, id) => await history.GetServiceStatusHistory(Request(id))),
            ["NodeChartHistory"] = new(t => t.Own.Id, async (history, _, id) =>
                await history.NodeChartHistory(new NodeChartRequest { NodeId = id.ToString(), From = DateTime.UtcNow.AddDays(-1), To = DateTime.UtcNow.AddDays(1) })),
        };

        public static TheoryData<string> Actions
        {
            get
            {
                var data = new TheoryData<string>();

                foreach (var name in Cases.Keys)
                    data.Add(name);

                return data;
            }
        }


        [Theory]
        [MemberData(nameof(Actions))]
        public async Task Outsider_GetsTheAnswerOfAnUnknownId(string action)
        {
            var tree = new WebAccessFixture();

            var refused = await Ask(tree, tree.Outsider, action, Cases[action].Target(tree));
            var unknown = await Ask(tree, tree.Outsider, action, Guid.NewGuid());

            Assert.Equal(unknown, refused);
        }

        [Theory]
        [MemberData(nameof(Actions))]
        public async Task Member_GetsTheData(string action)
        {
            var tree = new WebAccessFixture();

            var unknown = await Ask(tree, tree.Member, action, Guid.NewGuid());
            var served = await Ask(tree, tree.Member, action, Cases[action].Target(tree));

            Assert.NotEqual(unknown, served);
            Assert.DoesNotContain("throws:", served);
        }

        [Theory]
        [MemberData(nameof(Actions))]
        public async Task Admin_GetsTheData(string action)
        {
            var tree = new WebAccessFixture();

            var unknown = await Ask(tree, tree.Admin, action, Guid.NewGuid());
            var served = await Ask(tree, tree.Admin, action, Cases[action].Target(tree));

            Assert.NotEqual(unknown, served);
            Assert.DoesNotContain("throws:", served);
        }


        // ---- the history table: per-user state connected by HomeController.SelectNode

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Table_IsServedForTheSelectedSensor(bool admin)
        {
            var tree = new WebAccessFixture();
            var user = admin ? tree.Admin : tree.Member;
            var history = tree.History(user);

            await tree.Home(user).SelectNode(tree.Cpu.Id.ToString());

            Assert.NotNull(Table(await history.TableHistory(Request(tree.Cpu.Id))));
            Assert.NotNull(Table(await history.TabelHistoryLatest(Request(tree.Cpu.Id))));
            Assert.NotNull(Table(history.GetPreviousTablePage()));
            Assert.NotNull(Table(await history.GetNextTablePage()));
            Assert.IsType<FileContentResult>(await history.ExportHistory(tree.Cpu.Id.ToString(), 0, false, default, default));
        }

        [Fact]
        public async Task Table_IsNotServedToAnOutsider()
        {
            var tree = new WebAccessFixture();
            var history = tree.History(tree.Outsider);

            // The outsider opens the sensor by its id (a kept bookmark) and asks for everything the page asks.
            await tree.Home(tree.Outsider).SelectNode(tree.Cpu.Id.ToString());

            await AssertNoTable(history, tree.Cpu.Id);
        }

        [Fact]
        public async Task Table_StopsWhenTheRoleIsGone()
        {
            var tree = new WebAccessFixture();
            var history = tree.History(tree.Member);

            await tree.Home(tree.Member).SelectNode(tree.Cpu.Id.ToString());
            Assert.NotNull(Table(await history.TableHistory(Request(tree.Cpu.Id))));

            // The table stays connected in the user's state; it is checked again on every read.
            tree.Member.ProductsRoles.Clear();

            await AssertNoTable(history, tree.Cpu.Id);
        }

        [Fact]
        public async Task Table_OfTheOwnSensor_DoesNotAnswerARequestThatNamesAnotherProductsSensor()
        {
            var tree = new WebAccessFixture();
            var history = tree.History(tree.Outsider);

            await tree.Home(tree.Outsider).SelectNode(tree.ForeignCpu.Id.ToString());

            Assert.IsType<EmptyResult>(await history.TableHistory(Request(tree.Cpu.Id)));
            Assert.IsType<EmptyResult>(await history.TabelHistoryLatest(Request(tree.Cpu.Id)));
            Assert.IsType<NotFoundResult>(await history.ExportHistory(tree.Cpu.Id.ToString(), 0, false, default, default));

            // The refused request left the user's own table where it was.
            Assert.Equal(tree.ForeignCpu.Id.ToString(), tree.Outsider.History.Table.SensorId);
        }

        [Fact]
        public async Task Table_FollowsTheSensorTheRequestNames()
        {
            // The table in the user's state is the last selected sensor's, in any browser tab. A request
            // that names another sensor the user sees is answered with THAT sensor's rows.
            var tree = new WebAccessFixture();
            var history = tree.History(tree.Member);

            await tree.Home(tree.Member).SelectNode(tree.Mem.Id.ToString());

            var table = Assert.IsType<HistoryTableViewModel>(Table(await history.TableHistory(Request(tree.Cpu.Id))));

            Assert.Equal(tree.Cpu.Id.ToString(), table.SensorId);

            await tree.Home(tree.Member).SelectNode(tree.Mem.Id.ToString());

            var export = Assert.IsType<FileContentResult>(await history.ExportHistory(tree.Cpu.Id.ToString(), 0, false, default, default));

            Assert.StartsWith("own_cpu_", export.FileDownloadName);
            Assert.Equal(tree.Cpu.Id.ToString(), tree.Member.History.Table.SensorId);
        }


        private static async Task AssertNoTable(SensorHistoryController history, Guid sensorId)
        {
            Assert.IsType<EmptyResult>(await history.TableHistory(Request(sensorId)));
            Assert.IsType<EmptyResult>(await history.TabelHistoryLatest(Request(sensorId)));
            Assert.IsType<EmptyResult>(history.GetPreviousTablePage());
            Assert.IsType<EmptyResult>(await history.GetNextTablePage());
            Assert.IsType<NotFoundResult>(await history.ExportHistory(sensorId.ToString(), 0, false, default, default));
        }

        private static object Table(IActionResult result) => Assert.IsType<PartialViewResult>(result).Model;

        private static async Task<string> Ask(WebAccessFixture tree, User user, string action, Guid id)
        {
            string answer;

            try
            {
                answer = WebAccessFixture.Describe(await Cases[action].Call(tree.History(user), tree, id));
            }
            catch (Exception ex)
            {
                answer = $"throws:{ex.GetType().Name}";
            }

            return answer.Replace(id.ToString(), "<id>");
        }
    }
}
