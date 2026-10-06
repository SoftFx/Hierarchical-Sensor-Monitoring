using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    public class ProductControllerTests
    {
        private delegate void OutProductCallback(Guid id, out ProductModel product);

        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folderManager = new();
        private readonly Mock<IUserManager> _userManager = new();
        private readonly Mock<IChatsManager> _chats = new();

        private readonly ProductModel _root = new(EntitiesFactory.BuildProductEntity(name: "root") with { Id = Guid.NewGuid().ToString() });
        private readonly ProductModel _nested = new(EntitiesFactory.BuildProductEntity(name: "nested") with { Id = Guid.NewGuid().ToString() });


        public ProductControllerTests()
        {
            _root.AddSubProduct(_nested);

            SetupProduct(_root);
            SetupProduct(_nested);

            _cache.Setup(c => c.GetProducts()).Returns(new List<ProductModel>());
            _cache.Setup(c => c.RemoveProductAsync(It.IsAny<Guid>(), It.IsAny<InitiatorInfo>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _userManager.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns(new List<User>());
            _folderManager.Setup(f => f.GetUserFolders(It.IsAny<User>())).Returns(new List<FolderModel>());
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
    }
}
