using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.TaskResult;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // The General tab of the folder page (/Folders/EditFolder and /Folders/AddFolder, POST) saves the
    // list of the folder's products, and that list is a list of role grants: FolderManager gives every
    // user of a folder their folder role on a product that enters it and takes it away when the
    // product leaves. The folder role was checked, the products were not: the list comes from the
    // client and was resolved against the whole tree, so a folder's Manager who posted the id of a
    // product they held no role on became its Manager, and so did the folder's other users in their
    // roles. Each product that enters or leaves a folder must be one the caller manages.
    //
    // The folder manager is the real one, so the tests see the roles a request ends in, not a call.
    // Each request goes through the filters declared on the action (DeclaredFilters) and then the
    // action, as MVC runs it.
    public class FoldersControllerProductsTests : IAsyncLifetime
    {
        private delegate void OutProductCallback(Guid id, out ProductModel product);

        private const string FolderName = "folder";

        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IUserManager> _userManager = new();
        private readonly Mock<IChatsManager> _chats = new();

        private readonly FolderModel _folder = NewFolder(FolderName);
        private readonly FolderModel _otherFolder = NewFolder("other folder");

        // Named by what the caller (a Manager of _folder) is to each of them.
        private readonly ProductModel _managed = NewProduct("managed");
        private readonly ProductModel _managedNested = NewProduct("managed nested");
        private readonly ProductModel _viewed = NewProduct("viewed");
        private readonly ProductModel _foreign = NewProduct("foreign");
        private readonly ProductModel _managedInFolder;
        private readonly ProductModel _viewedInFolder;
        private readonly ProductModel _managedInOtherFolder;

        private readonly User _caller = new("caller");
        private readonly User _colleague = new("colleague");

        private FolderManager _folderManager;
        private TreeViewModel _tree;


        public FoldersControllerProductsTests()
        {
            _managedInFolder = NewProduct("managed in folder", _folder);
            _viewedInFolder = NewProduct("viewed in folder", _folder);
            _managedInOtherFolder = NewProduct("managed in other folder", _otherFolder);

            _managed.AddSubProduct(_managedNested);

            foreach (var product in new[] { _managed, _managedNested, _viewed, _foreign, _managedInFolder, _viewedInFolder, _managedInOtherFolder })
                _cache.Setup(c => c.TryGetProduct(product.Id, out It.Ref<ProductModel>.IsAny))
                    .Callback(new OutProductCallback((Guid _, out ProductModel p) => p = product))
                    .Returns(true);

            _cache.Setup(c => c.GetProducts())
                .Returns(new List<ProductModel> { _managed, _viewed, _foreign, _managedInFolder, _viewedInFolder, _managedInOtherFolder });
            _cache.Setup(c => c.UpdateProductAsync(It.IsAny<ProductUpdate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TaskResult.Ok);

            _userManager.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns(new List<User>());
            _userManager.Setup(u => u.UpdateUser(It.IsAny<User>())).ReturnsAsync(true);
            _chats.Setup(c => c.GetValues()).Returns([]);

            // The caller manages the folder and, by their own product roles, some of the products; of
            // two products they only view. The colleague is a Viewer of the folder and of nothing else.
            _caller.FoldersRoles.Add(_folder.Id, ProductRoleEnum.ProductManager);
            _caller.ProductsRoles.AddRange(
            [
                (_managed.Id, ProductRoleEnum.ProductManager),
                (_managedInFolder.Id, ProductRoleEnum.ProductManager),
                (_managedInOtherFolder.Id, ProductRoleEnum.ProductManager),
                (_viewed.Id, ProductRoleEnum.ProductViewer),
                (_viewedInFolder.Id, ProductRoleEnum.ProductViewer),
            ]);

            _colleague.FoldersRoles.Add(_folder.Id, ProductRoleEnum.ProductViewer);
        }

        public async Task InitializeAsync()
        {
            _folderManager = new FolderManager(new Mock<IDatabaseCore>().Object, _cache.Object, _userManager.Object,
                new Mock<IJournalService>().Object, _chats.Object);

            await _folderManager.TryAdd(_folder, InitiatorInfo.System);
            await _folderManager.TryAdd(_otherFolder, InitiatorInfo.System);

            _folder.UserRoles.Add(_caller, ProductRoleEnum.ProductManager);
            _folder.UserRoles.Add(_colleague, ProductRoleEnum.ProductViewer);

            // Built last: the tree puts each product into its folder as it reads the cache.
            _tree = new TreeViewModel(_cache.Object, _folderManager, _userManager.Object);
        }

        public Task DisposeAsync() => Task.CompletedTask;


        private static FolderModel NewFolder(string name) =>
            new(new FolderEntity
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = name,
                AuthorId = Guid.NewGuid().ToString(),
                CreationDate = DateTime.UtcNow.Ticks,
            });

        private static ProductModel NewProduct(string name, FolderModel folder = null) =>
            new(EntitiesFactory.BuildProductEntity(name: name) with { Id = Guid.NewGuid().ToString(), FolderId = folder?.Id.ToString() });

        private static User Admin() => new("admin") { IsAdmin = true };

        private static User FolderUser(Guid folderId, ProductRoleEnum role)
        {
            var user = new User("someone");
            user.FoldersRoles.Add(folderId, role);

            return user;
        }

        private FoldersController CreateController(User user) =>
            new(_folderManager, _userManager.Object, _tree, _chats.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = user },
                },
            };

        // The form as the page posts it: the products the folder keeps (hidden inputs, one per row of
        // the table) and the products picked in the "add" list.
        private static EditFolderViewModel Form(Guid folderId, IEnumerable<ProductModel> kept, IEnumerable<ProductModel> added, string name = FolderName) =>
            new()
            {
                Id = folderId,
                Name = name,
                OldName = FolderName,
                Description = "description",
                Color = Color.Red,
                Products = new FolderProductsViewModel
                {
                    Products = kept.Select(p => p.Id).ToList(),
                    SelectedProducts = added.Select(p => p.Id.ToString()).ToList(),
                },
            };

        // The folder's own two products are kept unless a test says otherwise.
        private EditFolderViewModel Adding(params ProductModel[] added) =>
            Form(_folder.Id, [_managedInFolder, _viewedInFolder], added);

        private EditFolderViewModel Keeping(params ProductModel[] kept) =>
            Form(_folder.Id, kept, []);

        private async Task<IActionResult> Edit(User user, EditFolderViewModel form)
        {
            var controller = CreateController(user);

            return DeclaredFilters.Refusal(controller, nameof(FoldersController.EditFolder), form)
                ?? await controller.EditFolder(form);
        }

        private async Task<IActionResult> Add(User user, EditFolderViewModel form)
        {
            var controller = CreateController(user);

            return DeclaredFilters.Refusal(controller, nameof(FoldersController.AddFolder), form)
                ?? await controller.AddFolder(form);
        }

        private static int StatusCodeOf(IActionResult result) =>
            result switch
            {
                StatusCodeResult codeResult => codeResult.StatusCode,
                ObjectResult objectResult => objectResult.StatusCode ?? 200,
                _ => throw new InvalidOperationException($"unexpected result type {result.GetType().Name}"),
            };

        // The role filter refuses a plain form post by sending the browser to the home page.
        private static void AssertSentHome(IActionResult result) =>
            Assert.Equal("Home", Assert.IsType<RedirectToActionResult>(result).ControllerName);

        private void VerifyEntered(ProductModel product, FolderModel folder) =>
            _cache.Verify(c => c.UpdateProductAsync(It.Is<ProductUpdate>(u => u.Id == product.Id && u.FolderId == folder.Id), It.IsAny<CancellationToken>()), Times.Once);

        private void VerifyLeft(ProductModel product) =>
            _cache.Verify(c => c.UpdateProductAsync(It.Is<ProductUpdate>(u => u.Id == product.Id && u.FolderId == Guid.Empty), It.IsAny<CancellationToken>()), Times.Once);

        // Nothing of a refused request may be applied: no product is touched, no role is handed out,
        // the folder keeps its products and its name.
        private void VerifyNothingChanged()
        {
            _cache.Verify(c => c.UpdateProductAsync(It.IsAny<ProductUpdate>(), It.IsAny<CancellationToken>()), Times.Never);
            _userManager.Verify(u => u.UpdateUser(It.IsAny<User>()), Times.Never);

            Assert.Equal(new[] { _managedInFolder.Id, _viewedInFolder.Id }.Order(), _folder.Products.Keys.Order());
            Assert.Equal(FolderName, _folder.Name);
            Assert.Equal(2, _folderManager.Count);
            Assert.Empty(_colleague.ProductsRoles);
        }


        [Fact]
        public async Task Edit_Admin_AddsAnyProduct_AndTheFoldersUsersReceiveTheirRolesOnIt()
        {
            var result = await Edit(Admin(), Adding(_foreign));

            Assert.IsType<ViewResult>(result);
            VerifyEntered(_foreign, _folder);
            Assert.Contains((_foreign.Id, ProductRoleEnum.ProductManager), _caller.ProductsRoles);
            Assert.Contains((_foreign.Id, ProductRoleEnum.ProductViewer), _colleague.ProductsRoles);
        }

        [Fact]
        public async Task Edit_FolderManager_AddsAProductTheyManage()
        {
            var result = await Edit(_caller, Adding(_managed));

            Assert.IsType<ViewResult>(result);
            VerifyEntered(_managed, _folder);
            Assert.Contains(_managed.Id, _folder.Products.Keys);
            Assert.Contains((_managed.Id, ProductRoleEnum.ProductViewer), _colleague.ProductsRoles);
        }

        [Fact]
        public async Task Edit_FolderManager_AddingAProductTheyHoldNoRoleOn_Gets403_AndReceivesNoRole()
        {
            // The request that used to end with the caller as a Manager of the product.
            var result = await Edit(_caller, Adding(_foreign));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            Assert.DoesNotContain(_caller.ProductsRoles, r => r.Item1 == _foreign.Id);
            VerifyNothingChanged();
        }

        [Fact]
        public async Task Edit_FolderManager_AddingAProductTheyOnlyView_Gets403()
        {
            // A Viewer may not hand the product's roles out to the folder's users either.
            var result = await Edit(_caller, Adding(_viewed));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Fact]
        public async Task Edit_OneRefusedProduct_RefusesTheWholeRequest()
        {
            // Neither the product the caller manages nor the new name is applied: a request that
            // names a product the caller may not touch is refused as a whole, not trimmed.
            var form = Form(_folder.Id, [_managedInFolder, _viewedInFolder], [_managed, _foreign], name: "renamed");

            var result = await Edit(_caller, form);

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Fact]
        public async Task Edit_FolderManager_RemovesAProductTheyManage_AndTheFoldersUsersLoseTheirRolesOnIt()
        {
            _colleague.ProductsRoles.Add((_managedInFolder.Id, ProductRoleEnum.ProductViewer));

            var result = await Edit(_caller, Keeping(_viewedInFolder));

            Assert.IsType<ViewResult>(result);
            VerifyLeft(_managedInFolder);
            Assert.DoesNotContain(_managedInFolder.Id, _folder.Products.Keys);
            Assert.Empty(_colleague.ProductsRoles);
        }

        [Fact]
        public async Task Edit_FolderManager_RemovingAProductTheyOnlyView_Gets403()
        {
            // A product leaving a folder takes the folder's roles on it away from its users.
            var result = await Edit(_caller, Keeping(_managedInFolder));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Fact]
        public async Task Edit_FolderManager_SavesTheFolderWithItsProductsUnchanged()
        {
            // No product enters or leaves, so the product the caller only views is no obstacle.
            var result = await Edit(_caller, Form(_folder.Id, [_managedInFolder, _viewedInFolder], [], name: "renamed"));

            Assert.IsType<ViewResult>(result);
            Assert.Equal("renamed", _folder.Name);
            _cache.Verify(c => c.UpdateProductAsync(It.IsAny<ProductUpdate>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Edit_UnknownProductId_IsLeftOut_AsBefore()
        {
            var gone = NewProduct("gone");

            var result = await Edit(_caller, Adding(gone, _managed));

            Assert.IsType<ViewResult>(result);
            VerifyEntered(_managed, _folder);
            Assert.DoesNotContain(gone.Id, _folder.Products.Keys);
        }

        [Fact]
        public async Task Edit_NestedNode_IsNotAProductOfAFolder()
        {
            // Roles are held per root product, so the Manager of the root holds none on the node's id.
            var byManager = await Edit(_caller, Adding(_managedNested));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(byManager));
            VerifyNothingChanged();

            var byAdmin = await Edit(Admin(), Adding(_managedNested));

            Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(byAdmin));
            VerifyNothingChanged();
        }

        [Fact]
        public async Task Edit_ProductOfAnotherFolder_Is400()
        {
            // The page offers only products outside any folder. Taking one from another folder here
            // would leave it listed in both.
            var byManager = await Edit(_caller, Adding(_managedInOtherFolder));

            Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(byManager));
            VerifyNothingChanged();

            var byAdmin = await Edit(Admin(), Adding(_managedInOtherFolder));

            Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(byAdmin));
            VerifyNothingChanged();
        }

        [Fact]
        public async Task Edit_FolderViewer_IsRefusedByTheFolderRole()
        {
            var viewer = FolderUser(_folder.Id, ProductRoleEnum.ProductViewer);
            viewer.ProductsRoles.Add((_managed.Id, ProductRoleEnum.ProductManager));

            var result = await Edit(viewer, Adding(_managed));

            AssertSentHome(result);
            VerifyNothingChanged();
        }

        [Fact]
        public async Task Edit_ManagerOfAnotherFolder_IsRefusedByTheFolderRole()
        {
            var neighbour = FolderUser(_otherFolder.Id, ProductRoleEnum.ProductManager);
            neighbour.ProductsRoles.Add((_managed.Id, ProductRoleEnum.ProductManager));

            var result = await Edit(neighbour, Adding(_managed));

            AssertSentHome(result);
            VerifyNothingChanged();
        }

        [Fact]
        public async Task Edit_UserWithoutAnyRole_IsRefusedByTheFolderRole()
        {
            var result = await Edit(new User("stranger"), Adding(_managed));

            AssertSentHome(result);
            VerifyNothingChanged();
        }

        [Fact]
        public async Task Edit_UnknownFolder_Is404()
        {
            var result = await Edit(Admin(), Form(Guid.NewGuid(), [], [_managed]));

            Assert.Equal(StatusCodes.Status404NotFound, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Fact]
        public void EditPage_OffersOnlyTheProductsTheCallerManages()
        {
            // What the page offers and what the save accepts are one rule: a product outside any
            // folder that the caller manages. A product they only view is not in the list.
            var controller = CreateController(_caller);

            var page = Assert.IsType<ViewResult>(controller.EditFolder(_folder.Id));

            var offered = Assert.IsType<EditFolderViewModel>(page.Model).Products.AvailableProducts.Select(p => p.Value);
            Assert.Equal([_managed.Id.ToString()], offered);
        }


        [Fact]
        public async Task Add_Admin_CreatesTheFolderWithItsProducts()
        {
            var result = await Add(Admin(), Form(Guid.Empty, [], [_managed, _foreign], name: "new folder"));

            Assert.IsType<RedirectToActionResult>(result);
            Assert.True(_folderManager.TryGetIdByName("new folder", out var newFolderId));
            Assert.True(_folderManager.TryGetValue(newFolderId, out var newFolder));
            VerifyEntered(_managed, newFolder);
            VerifyEntered(_foreign, newFolder);
        }

        [Fact]
        public async Task Add_FolderManager_PostingTheirOwnFoldersId_Gets403_AndNoFolderIsCreated()
        {
            // The request that used to pass: the role was checked on the posted folder id, which the
            // action does not use, and the new folder took the listed products with it. Folders are
            // created by admins only; the page opens in "add" mode for nobody else.
            var result = await Add(_caller, Form(_folder.Id, [], [_foreign], name: "new folder"));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Theory]
        [InlineData(ProductRoleEnum.ProductManager)]
        [InlineData(ProductRoleEnum.ProductViewer)]
        public async Task Add_NonAdmin_Gets403_WhateverTheyManage(ProductRoleEnum folderRole)
        {
            var user = FolderUser(_folder.Id, folderRole);
            user.ProductsRoles.Add((_managed.Id, ProductRoleEnum.ProductManager));

            var result = await Add(user, Form(Guid.Empty, [], [_managed], name: "new folder"));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Fact]
        public async Task Add_UserWithoutAnyRole_Gets403()
        {
            var result = await Add(new User("stranger"), Form(Guid.Empty, [], [], name: "new folder"));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingChanged();
        }

        [Fact]
        public async Task Add_NestedNodeOrProductOfAnotherFolder_Is400_AndNoFolderIsCreated()
        {
            var withNested = await Add(Admin(), Form(Guid.Empty, [], [_managedNested], name: "new folder"));

            Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(withNested));
            VerifyNothingChanged();

            var withTaken = await Add(Admin(), Form(Guid.Empty, [], [_managedInOtherFolder], name: "new folder"));

            Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(withTaken));
            VerifyNothingChanged();
        }
    }
}
