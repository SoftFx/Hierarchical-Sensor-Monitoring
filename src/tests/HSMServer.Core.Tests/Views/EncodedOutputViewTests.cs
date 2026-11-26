using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.History;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.UserTreeShallowCopy;
using HSMServer.Model.ViewModel;
using HSMServer.Notifications.Chats;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Views
{
    // Names and paths come from outside: a sensor path from whoever holds an access key of the product,
    // a folder or key name from a manager. The views render them as text. Each test renders one compiled
    // view with a name made of markup and asserts the name arrives HTML-encoded: no element of the name
    // reaches the page.
    public class EncodedOutputViewTests
    {
        private const string Markup = "<img src=x onerror=alert(1)>";
        private const string Encoded = "&lt;img src=x onerror=alert(1)&gt;";

        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folders = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly User _admin = new("admin") { IsAdmin = true };

        private readonly FolderModel _folder;
        private readonly ProductModel _product;
        private readonly BaseSensorModel _sensor;
        private readonly BaseSensorModel _file;
        private readonly BaseSensorModel _quoted;
        private readonly TreeViewModel _tree;


        public EncodedOutputViewTests()
        {
            // A name short enough not to be cut by the tree (GetShortNodeName).
            _folder = new FolderModel(EntitiesFactory.BuildFolderEntity() with { DisplayName = Markup });
            _product = new ProductModel(EntitiesFactory.BuildProductEntity(name: Markup) with
            {
                Id = Guid.NewGuid().ToString(),
                FolderId = _folder.Id.ToString(),
            });

            _sensor = AddSensor(Markup, SensorType.Integer);
            _file = AddSensor("report", SensorType.File);
            _quoted = AddSensor("say \"hi\"", SensorType.Integer);

            var key = new AccessKeyModel(EntitiesFactory.BuildAccessKeyEntity(name: Markup, productId: _product.Id.ToString()));
            _product.AccessKeys.TryAdd(key.Id, key);

            var folder = _folder;
            _folders.Setup(f => f.TryGetValueById(_folder.Id, out It.Ref<FolderModel>.IsAny))
                .Returns(true)
                .Callback(new TryGetFolderById((Guid? _, out FolderModel found) => found = folder));

            _cache.Setup(c => c.GetProducts()).Returns([_product]);
            _users.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns([]);
            _users.Setup(u => u.GetManagers(It.IsAny<Guid>())).Returns([new User(Markup)]);

            _tree = new TreeViewModel(_cache.Object, _folders.Object, _users.Object);
        }


        [Fact]
        public async Task TreeSensor_ShowsTheSensorNameAsText()
        {
            var html = await Render("~/Views/Tree/_TreeSensor.cshtml", new SensorShallowModel(_tree.Sensors[_sensor.Id], _admin));

            AssertEncoded(html);
        }

        [Fact]
        public void TreeMetadata_CarriesANameWithQuotesAsOneJsonString()
        {
            // data-jstree is JSON the tree script parses: the title is written as a JSON string, so a
            // quote in a name stays part of the name.
            var json = new SensorShallowModel(_tree.Sensors[_quoted.Id], _admin).ToJSTree();

            using var document = System.Text.Json.JsonDocument.Parse(json);

            Assert.Equal("say \"hi\"", document.RootElement.GetProperty("title").GetString());
        }

        [Fact]
        public async Task TreeNode_ShowsNodeAndSensorNamesAsText()
        {
            var html = await Render("~/Views/Tree/_TreeNode.cshtml", BuildNode());

            AssertEncoded(html);
        }

        [Fact]
        public async Task TreeFolder_ShowsTheFolderNameAsText()
        {
            var folder = new FolderShallowModel(_folder, _admin);

            folder.AddChild(BuildNode(), _admin);

            var html = await Render("~/Views/Tree/_TreeFolder.cshtml", folder);

            AssertEncoded(html);
        }

        [Fact]
        public async Task SensorPage_ShowsThePathInTheHeaderAsText()
        {
            var html = await Render("~/Views/Home/_MetaInfo.cshtml", new SensorInfoViewModel(_tree.Sensors[_sensor.Id]));

            Assert.Contains($"{Encoded}/{Encoded}", html);
            AssertEncoded(html);
        }

        [Fact]
        public async Task AccessKeysTables_ShowProductPathAndKeyNameAsText()
        {
            foreach (var fullTable in new[] { true, false })
            {
                var model = new AccessKeyTableViewModel { FullTable = fullTable, Keys = [.. _tree.AccessKeys.Values] };

                var html = await Render("~/Views/AccessKeys/_AllAccessKeys.cshtml", model);

                AssertEncoded(html);
            }
        }

        [Fact]
        public async Task ProductList_ShowsProductAndFolderNamesAsText()
        {
            var model = new FolderViewModel(_folder, [new ProductViewModel(_tree.Nodes[_product.Id], _users.Object)]);

            AssertEncoded(await Render("~/Views/Product/_ProductList.cshtml", model));
            AssertEncoded(await Render("~/Views/Product/_FolderAccordion.cshtml", model));
        }

        [Fact]
        public async Task FolderPage_ShowsItsProductNamesAsText()
        {
            var model = new FolderProductsViewModel([], []);

            model.InitFolderProducts(new Dictionary<Guid, ProductNodeViewModel> { [_product.Id] = _tree.Nodes[_product.Id] });

            AssertEncoded(await Render("~/Views/Folders/_Products.cshtml", model));
        }

        [Fact]
        public async Task ChatPage_ShowsItsFolderNamesAsText()
        {
            var model = new HSMServer.Model.Notifications.ChatFoldersViewModel([_folder], [_folder]);

            AssertEncoded(await Render("~/Views/Notifications/_ChatFolders.cshtml", model));
        }

        [Fact]
        public async Task RecentFiles_KeepTheFileExtensionOutOfScript()
        {
            // An extension is free text from the collector. It used to be written into inline handlers as a
            // quoted script string; now it travels as a data attribute, which is only ever read as text. The list is a
            // page of the File sensor page (#1886): its handlers live in _SensorFileContent and read the attributes.
            const string extension = "x'+alert(1)+'";

            var page = new HSMServer.Model.FileSensors.FileValuesPageViewModel
            {
                EncodedId = _file.Id.ToString(),
                Rows =
                [
                    HSMServer.Model.FileSensors.FileValueRowViewModel.From(new FileValue { Name = "first", Extension = "txt", Value = [], Time = DateTime.UtcNow }),
                    HSMServer.Model.FileSensors.FileValueRowViewModel.From(new FileValue { Name = "second", Extension = extension, Value = [], Time = DateTime.UtcNow.AddSeconds(1) }),
                ],
            };

            var html = await Render("~/Views/Home/_FileAccordions.cshtml", page);

            Assert.Contains("data-extension='x&#x27;&#x2B;alert(1)&#x2B;&#x27;'", html);

            // No handler attribute at all, so none carries the extension.
            Assert.DoesNotContain("onclick", html);
        }


        private static void AssertEncoded(string html)
        {
            Assert.Contains(Encoded, html);
            Assert.DoesNotContain("<img src=x", html);

            // Encoded once: a second pass would show the entities themselves.
            Assert.DoesNotContain("&amp;lt;img", html);
        }

        private Task<string> Render(string view, object model) =>
            ViewRenderer.RenderAsync(view, model, _admin, services =>
            {
                services.AddSingleton(_users.Object);
                var chats = new Mock<IChatsManager>();

                chats.Setup(c => c.GetValues()).Returns([]);
                services.AddSingleton(chats.Object);
            });

        private NodeShallowModel BuildNode()
        {
            var node = new NodeShallowModel(_tree.Nodes[_product.Id], _admin, _ => true, _ => true);
            var sensor = node.AddChild(new SensorShallowModel(_tree.Sensors[_sensor.Id], _admin), _admin);

            node.ToRenderNode(sensor.Id);

            return node;
        }

        private BaseSensorModel AddSensor(string name, SensorType type)
        {
            var sensor = Infrastructure.SensorModelFactory.Build(
                EntitiesFactory.BuildSensorEntity(name: name, type: (byte)type) with { Id = Guid.NewGuid().ToString() });

            _product.AddSensor(sensor);
            _cache.Setup(c => c.GetSensor(sensor.Id)).Returns(sensor);

            return sensor;
        }


        private delegate void TryGetFolderById(Guid? id, out FolderModel folder);
    }
}
