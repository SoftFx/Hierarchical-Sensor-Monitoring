using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.TaskResult;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Filters;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Controls;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // #1512: /Product/RemoveProduct deleted any product or nested node for any signed-in user, over
    // GET. It must now require Manager of the node's root product (or admin), and be POST +
    // antiforgery only. Invoked directly, so the HTTP-method and antiforgery gates are asserted as
    // the surface contract they are (the AlertTemplatesControllerTests pattern): MVC answers a GET to
    // a POST-only action without running it.
    //
    // The second half covers the other writes of the controller, which had the same hole: the edit
    // form of a product, the three role actions of its Members table and the move between folders.
    public class ProductControllerTests
    {
        private delegate void OutProductCallback(Guid id, out ProductModel product);

        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folderManager = new();
        private readonly Mock<IUserManager> _userManager = new();
        private readonly Mock<IChatsManager> _chats = new();

        private readonly ProductModel _root = new(EntitiesFactory.BuildProductEntity(name: "root") with { Id = Guid.NewGuid().ToString() });
        private readonly ProductModel _nested = new(EntitiesFactory.BuildProductEntity(name: "nested") with { Id = Guid.NewGuid().ToString() });

        // Two levels below the root: here the root and the parent are different nodes, so a gate that
        // looked at the parent instead of the root would be caught.
        private readonly ProductModel _leaf = new(EntitiesFactory.BuildProductEntity(name: "leaf") with { Id = Guid.NewGuid().ToString() });

        // The user the role actions are aimed at: a Viewer of the root and, as stale data may have it,
        // of the nodes below it too, so that every role action has an entry to change.
        private readonly User _target = new("target");


        public ProductControllerTests()
        {
            _root.AddSubProduct(_nested);
            _nested.AddSubProduct(_leaf);

            SetupProduct(_root);
            SetupProduct(_nested);
            SetupProduct(_leaf);

            _target.ProductsRoles.Add((_root.Id, ProductRoleEnum.ProductViewer));
            _target.ProductsRoles.Add((_nested.Id, ProductRoleEnum.ProductViewer));
            _target.ProductsRoles.Add((_leaf.Id, ProductRoleEnum.ProductViewer));

            // The view-model tree is built from the cache: the root brings its nested node along.
            _cache.Setup(c => c.GetProducts()).Returns(new List<ProductModel> { _root });
            _cache.Setup(c => c.RemoveProductAsync(It.IsAny<Guid>(), It.IsAny<InitiatorInfo>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _cache.Setup(c => c.UpdateProductAsync(It.IsAny<ProductUpdate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TaskResult.Ok);
            _userManager.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns(new List<User>());
            _userManager.Setup(u => u[_target.Id]).Returns(_target);
            _userManager.Setup(u => u.UpdateUser(It.IsAny<User>())).ReturnsAsync(true);
            _folderManager.Setup(f => f.GetUserFolders(It.IsAny<User>())).Returns(new List<FolderModel>());
            _folderManager.Setup(f => f.MoveProduct(It.IsAny<ProductNodeViewModel>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<InitiatorInfo>()))
                .Returns(Task.CompletedTask);
            _chats.Setup(c => c.GetValues()).Returns([]);
        }


        private void SetupProduct(ProductModel product) =>
            _cache.Setup(c => c.TryGetProduct(product.Id, out It.Ref<ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid _, out ProductModel p) => p = product))
                .Returns(true);

        private ProductController CreateController(User user) =>
            new(_userManager.Object, _cache.Object, _folderManager.Object,
                new TreeViewModel(_cache.Object, _folderManager.Object, _userManager.Object),
                _chats.Object, NullLogger<ProductController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = user },
                },
            };

        private static User UserWith(Guid productId, ProductRoleEnum role) =>
            new("someone") { ProductsRoles = { (productId, role) } };

        private static int StatusCodeOf(IActionResult result) =>
            result switch
            {
                StatusCodeResult codeResult => codeResult.StatusCode,
                ObjectResult objectResult => objectResult.StatusCode ?? 200,
                _ => throw new InvalidOperationException($"unexpected result type {result.GetType().Name}"),
            };

        private void VerifyNothingRemoved() =>
            _cache.Verify(c => c.RemoveProductAsync(It.IsAny<Guid>(), It.IsAny<InitiatorInfo>(), It.IsAny<CancellationToken>()), Times.Never);


        [Fact]
        public void RemoveProduct_IsPostOnly_WithAntiforgery()
        {
            var method = typeof(ProductController).GetMethod(nameof(ProductController.RemoveProduct), BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(method);
            Assert.True(method.IsDefined(typeof(HttpPostAttribute), inherit: false),
                "RemoveProduct deletes a subtree irreversibly and must not answer GET (a cross-site link rides the SameSite=Lax cookie)");
            Assert.False(method.IsDefined(typeof(HttpGetAttribute), inherit: false));
            Assert.True(method.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), inherit: false),
                "RemoveProduct must require an antiforgery token");
        }

        [Fact]
        public async Task Viewer_Gets403_AndNothingIsRemoved()
        {
            var result = await CreateController(UserWith(_root.Id, ProductRoleEnum.ProductViewer)).RemoveProduct(_root.Id);

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingRemoved();
        }

        [Fact]
        public async Task UserWithoutAnyRole_Gets403_AndNothingIsRemoved()
        {
            var result = await CreateController(new User("stranger")).RemoveProduct(_root.Id);

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingRemoved();
        }

        [Fact]
        public async Task ManagerOfAnotherProduct_Gets403_AndNothingIsRemoved()
        {
            var result = await CreateController(UserWith(Guid.NewGuid(), ProductRoleEnum.ProductManager)).RemoveProduct(_root.Id);

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingRemoved();
        }

        [Fact]
        public async Task NestedNode_IsJudgedByItsRootProduct()
        {
            // A Viewer of the root may not remove a node below it either: RemoveProductAsync takes any
            // node id, so the role is checked on the root product, as HomeController.RemoveNode does.
            var viewer = await CreateController(UserWith(_root.Id, ProductRoleEnum.ProductViewer)).RemoveProduct(_nested.Id);

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(viewer));
            VerifyNothingRemoved();

            var manager = await CreateController(UserWith(_root.Id, ProductRoleEnum.ProductManager)).RemoveProduct(_nested.Id);

            Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(manager));
            _cache.Verify(c => c.RemoveProductAsync(_nested.Id, It.IsAny<InitiatorInfo>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Manager_RemovesTheProduct_AsThemselves()
        {
            InitiatorInfo initiator = null;
            _cache.Setup(c => c.RemoveProductAsync(_root.Id, It.IsAny<InitiatorInfo>(), It.IsAny<CancellationToken>()))
                .Callback<Guid, InitiatorInfo, CancellationToken>((_, i, _) => initiator = i)
                .Returns(Task.CompletedTask);

            var result = await CreateController(UserWith(_root.Id, ProductRoleEnum.ProductManager)).RemoveProduct(_root.Id);

            Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
            _cache.Verify(c => c.RemoveProductAsync(_root.Id, It.IsAny<InitiatorInfo>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.NotNull(initiator);
        }

        [Fact]
        public async Task Admin_RemovesAnyProduct()
        {
            var result = await CreateController(new User("admin") { IsAdmin = true }).RemoveProduct(_root.Id);

            Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
            _cache.Verify(c => c.RemoveProductAsync(_root.Id, It.IsAny<InitiatorInfo>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UnknownId_Is404_AndNothingIsRemoved()
        {
            var result = await CreateController(new User("admin") { IsAdmin = true }).RemoveProduct(Guid.NewGuid());

            Assert.Equal(StatusCodes.Status404NotFound, StatusCodeOf(result));
            VerifyNothingRemoved();
        }

        // ---- The edit form, the role actions and the move ------------------------------------------

        private const string Edit = nameof(ProductController.EditProduct);
        private const string AddRight = nameof(ProductController.AddUserRight);
        private const string EditRole = nameof(ProductController.EditUserRole);
        private const string RemoveRole = nameof(ProductController.RemoveUserRole);
        private const string Move = nameof(ProductController.MoveProduct);

        private static readonly Guid FromFolder = Guid.NewGuid();
        private static readonly Guid ToFolder = Guid.NewGuid();

        public static TheoryData<string> GatedActions => [Edit, AddRight, EditRole, RemoveRole, Move];

        // The gated actions that take a node below the root. The move is not one: a folder holds root products.
        public static TheoryData<string> NodeActions => [Edit, AddRight, EditRole, RemoveRole];

        public static TheoryData<string> RoleActions => [AddRight, EditRole, RemoveRole];


        private static User Admin() => new("admin") { IsAdmin = true };

        private UserRightViewModel Right(Guid productId, Guid? userId = null) =>
            new() { UserId = userId ?? _target.Id, EntityId = productId, ProductRole = (int)ProductRoleEnum.ProductManager };

        private static IActionResult PostRole(ProductController controller, string action, UserRightViewModel model) =>
            action switch
            {
                AddRight => controller.AddUserRight(model),
                EditRole => controller.EditUserRole(model),
                RemoveRole => controller.RemoveUserRole(model),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
            };

        // One request of each action against the given product id, with the body the page would send.
        private async Task<IActionResult> Post(string action, User caller, Guid productId)
        {
            var controller = CreateController(caller);

            return action switch
            {
                Edit => await controller.EditProduct(new ProductGeneralInfoViewModel
                {
                    Id = productId,
                    Name = "renamed",
                    OldName = "root",
                    Description = "changed",
                    DefaultChats = new DefaultChatViewModel(),
                }),
                Move => await controller.MoveProduct(productId, FromFolder, ToFolder),
                _ => PostRole(controller, action, Right(productId)),
            };
        }

        // The state every gated action could have touched, as it stands when a request is refused.
        private void VerifyNothingChanged()
        {
            _cache.Verify(c => c.UpdateProductAsync(It.IsAny<ProductUpdate>(), It.IsAny<CancellationToken>()), Times.Never);
            _userManager.Verify(u => u.UpdateUser(It.IsAny<User>()), Times.Never);
            _folderManager.Verify(f => f.MoveProduct(It.IsAny<ProductNodeViewModel>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<InitiatorInfo>()), Times.Never);

            Assert.Equal(
                [(_root.Id, ProductRoleEnum.ProductViewer), (_nested.Id, ProductRoleEnum.ProductViewer), (_leaf.Id, ProductRoleEnum.ProductViewer)],
                _target.ProductsRoles);
        }

        // What the action writes when it is let through, for the product id it was posted with.
        private void VerifyChanged(string action, Guid productId)
        {
            switch (action)
            {
                case Edit:
                    _cache.Verify(c => c.UpdateProductAsync(
                        It.Is<ProductUpdate>(u => u.Id == productId && u.Name == "renamed" && u.Description == "changed"),
                        It.IsAny<CancellationToken>()), Times.Once);
                    break;

                case AddRight:
                    Assert.Contains((productId, ProductRoleEnum.ProductManager), _target.ProductsRoles);
                    _userManager.Verify(u => u.UpdateUser(_target), Times.Once);
                    break;

                case EditRole:
                    Assert.Equal(ProductRoleEnum.ProductManager, Assert.Single(_target.ProductsRoles, r => r.Item1 == productId).Item2);
                    _userManager.Verify(u => u.UpdateUser(_target), Times.Once);
                    break;

                case RemoveRole:
                    Assert.DoesNotContain(_target.ProductsRoles, r => r.Item1 == productId);
                    _userManager.Verify(u => u.UpdateUser(_target), Times.Once);
                    break;

                case Move:
                    _folderManager.Verify(f => f.MoveProduct(
                        It.Is<ProductNodeViewModel>(p => p.Id == productId), FromFolder, ToFolder, It.IsAny<InitiatorInfo>()), Times.Once);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }


        [Theory]
        [MemberData(nameof(GatedActions))]
        public void GatedAction_IsPostOnly(string action)
        {
            var overloads = typeof(ProductController).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == action).ToList();
            var post = Assert.Single(overloads, m => m.IsDefined(typeof(HttpPostAttribute), inherit: false));

            // POST and no second verb next to it: MVC would answer both.
            Assert.IsType<HttpPostAttribute>(Assert.Single(post.GetCustomAttributes<HttpMethodAttribute>(inherit: false)));

            // EditProduct also has the GET of the page, which carries the role filter; nothing else
            // may answer under the name of a write.
            Assert.All(overloads.Where(m => m != post), m =>
                Assert.Contains(m.GetCustomAttributes(inherit: false), a => a is UserRoleFilterBase));
        }

        [Theory]
        [InlineData(Edit)]
        [InlineData(Move)]
        public void FormAction_RequiresAnAntiforgeryToken(string action)
        {
            // The two actions a form or a link of the page reaches. The role actions take a JSON body,
            // which a cross-site form cannot send, and their callers send no token.
            var post = typeof(ProductController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(m => m.Name == action && m.IsDefined(typeof(HttpPostAttribute), inherit: false));

            Assert.True(post.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), inherit: false));
        }

        [Theory]
        [MemberData(nameof(RoleActions))]
        public void RoleAction_ValidatesNoAntiforgeryToken(string action)
        {
            // Pinned on purpose: EditProduct.cshtml calls these with $.ajax and a JSON body and sends
            // no token, so the attribute would turn every button of the Members table into a 400.
            // Whoever adds it must add the header to the three callers in the same change.
            var post = typeof(ProductController).GetMethod(action, BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(post);
            Assert.False(post.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), inherit: false));
        }

        [Theory]
        [MemberData(nameof(GatedActions))]
        public async Task GatedAction_Viewer_Gets403_AndNothingChanges(string action)
        {
            var result = await Post(action, UserWith(_root.Id, ProductRoleEnum.ProductViewer), _root.Id);

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Theory]
        [MemberData(nameof(GatedActions))]
        public async Task GatedAction_UserWithoutAnyRole_Gets403_AndNothingChanges(string action)
        {
            var result = await Post(action, new User("stranger"), _root.Id);

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Theory]
        [MemberData(nameof(GatedActions))]
        public async Task GatedAction_ManagerOfAnotherProduct_Gets403_AndNothingChanges(string action)
        {
            var result = await Post(action, UserWith(Guid.NewGuid(), ProductRoleEnum.ProductManager), _root.Id);

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Theory]
        [MemberData(nameof(GatedActions))]
        public async Task GatedAction_UnknownProduct_Is404_AndNothingChanges(string action)
        {
            var result = await Post(action, Admin(), Guid.NewGuid());

            Assert.Equal(StatusCodes.Status404NotFound, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Theory]
        [MemberData(nameof(GatedActions))]
        public async Task GatedAction_Manager_IsLetThrough(string action)
        {
            await Post(action, UserWith(_root.Id, ProductRoleEnum.ProductManager), _root.Id);

            VerifyChanged(action, _root.Id);
        }

        [Theory]
        [MemberData(nameof(GatedActions))]
        public async Task GatedAction_Admin_IsLetThrough(string action)
        {
            await Post(action, Admin(), _root.Id);

            VerifyChanged(action, _root.Id);
        }

        [Theory]
        [MemberData(nameof(GatedActions))]
        public async Task GatedAction_NestedNode_ViewerOfTheRoot_Gets403(string action)
        {
            var result = await Post(action, UserWith(_root.Id, ProductRoleEnum.ProductViewer), _nested.Id);

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Theory]
        [MemberData(nameof(GatedActions))]
        public async Task GatedAction_NestedNode_RoleOnTheNodeItself_CountsForNothing(string action)
        {
            // Roles are held per root product. An entry on the node's own id (the role actions write
            // whatever id they are posted) must not open the node: only the root's Manager passes.
            var result = await Post(action, UserWith(_nested.Id, ProductRoleEnum.ProductManager), _nested.Id);

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Theory]
        [MemberData(nameof(NodeActions))]
        public async Task GatedAction_NestedNode_ManagerOfTheRoot_IsLetThrough(string action)
        {
            await Post(action, UserWith(_root.Id, ProductRoleEnum.ProductManager), _nested.Id);

            VerifyChanged(action, _nested.Id);
        }

        [Fact]
        public async Task MoveProduct_OfANodeBelowTheRoot_IsRefused_ForItsManagerAndForAnAdmin()
        {
            // A folder holds root products. A node below the root stays in its product whoever asks.
            foreach (var caller in new[] { UserWith(_root.Id, ProductRoleEnum.ProductManager), Admin() })
                foreach (var node in new[] { _nested, _leaf })
                {
                    var result = await Post(Move, caller, node.Id);

                    Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
                }

            VerifyNothingChanged();
        }

        [Theory]
        [MemberData(nameof(NodeActions))]
        public async Task GatedAction_NodeTwoLevelsDown_ManagerOfTheRoot_IsLetThrough(string action)
        {
            await Post(action, UserWith(_root.Id, ProductRoleEnum.ProductManager), _leaf.Id);

            VerifyChanged(action, _leaf.Id);
        }

        [Theory]
        [MemberData(nameof(GatedActions))]
        public async Task GatedAction_NodeTwoLevelsDown_RoleOnItsParent_CountsForNothing(string action)
        {
            // The role is read on the root, not on the node above: a Manager entry on the node in
            // between opens nothing below it.
            var result = await Post(action, UserWith(_nested.Id, ProductRoleEnum.ProductManager), _leaf.Id);

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Fact]
        public async Task EditProduct_WritesAsTheCaller_AndAnswersWithTheForm()
        {
            ProductUpdate update = null;
            _cache.Setup(c => c.UpdateProductAsync(It.IsAny<ProductUpdate>(), It.IsAny<CancellationToken>()))
                .Callback<ProductUpdate, CancellationToken>((u, _) => update = u)
                .ReturnsAsync(TaskResult.Ok);

            var result = await Post(Edit, UserWith(_root.Id, ProductRoleEnum.ProductManager), _root.Id);

            // The fragment the page puts back in place of the form.
            Assert.Equal("_EditProductGeneralInfo", Assert.IsType<PartialViewResult>(result).ViewName);
            Assert.Equal(InitiatorInfo.AsUser("someone"), update?.Initiator);
        }

        [Theory]
        [MemberData(nameof(RoleActions))]
        public void RoleAction_UnknownUser_Is404_AndNothingChanges(string action)
        {
            var controller = CreateController(UserWith(_root.Id, ProductRoleEnum.ProductManager));

            var result = PostRole(controller, action, Right(_root.Id, userId: Guid.NewGuid()));

            Assert.Equal(StatusCodes.Status404NotFound, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Theory]
        [MemberData(nameof(RoleActions))]
        public void RoleAction_UnreadableBody_Is400(string action)
        {
            // MVC binds a body it cannot read to null; the gate must answer, not dereference it.
            var result = PostRole(CreateController(Admin()), action, model: null);

            Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Theory]
        [MemberData(nameof(RoleActions))]
        public void RoleAction_RefusedCaller_CannotLearnWhichUsersExist(string action)
        {
            // The caller is judged before the user is looked up: a known and an unknown user id get
            // the same 403.
            var controller = CreateController(UserWith(_root.Id, ProductRoleEnum.ProductViewer));

            var result = PostRole(controller, action, Right(_root.Id, userId: Guid.NewGuid()));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            _userManager.Verify(u => u[It.IsAny<Guid>()], Times.Never);
        }

        [Fact]
        public void Viewer_CannotMakeThemselvesManager()
        {
            // The shortest way through the old hole: a Viewer posting their own id.
            var viewer = UserWith(_root.Id, ProductRoleEnum.ProductViewer);
            _userManager.Setup(u => u[viewer.Id]).Returns(viewer);
            var controller = CreateController(viewer);

            var added = controller.AddUserRight(Right(_root.Id, userId: viewer.Id));
            var edited = controller.EditUserRole(Right(_root.Id, userId: viewer.Id));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(added));
            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(edited));
            Assert.False(viewer.IsManager(_root.Id));
            VerifyNothingChanged();
        }
    }
}
