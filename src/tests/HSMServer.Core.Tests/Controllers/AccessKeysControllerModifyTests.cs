using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // /AccessKeys/ModifyAccessKey (POST) renames an access key and replaces its permissions. The form
    // posts two ids, the key's and the product's, and the role used to be checked on the product id
    // while the write went by the key id: a Manager of one product who posted their own product id
    // next to another key's id changed that key, a key of another product or a master key included.
    // The role must be judged on the product the STORED key belongs to. Each request goes through the
    // filters declared on the action (DeclaredFilters) and then the action, as MVC runs it.
    public class AccessKeysControllerModifyTests
    {
        private delegate void OutProductCallback(Guid id, out ProductModel product);

        private const string NewName = "renamed";

        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folderManager = new();
        private readonly Mock<IUserManager> _userManager = new();

        private readonly ProductModel _product = NewProduct("product");
        private readonly ProductModel _nested = NewProduct("nested");
        private readonly ProductModel _otherProduct = NewProduct("other");

        private readonly AccessKeyModel _key;
        private readonly AccessKeyModel _nestedKey;
        private readonly AccessKeyModel _otherKey;
        private readonly AccessKeyModel _masterKey;

        private readonly TreeViewModel _tree;


        public AccessKeysControllerModifyTests()
        {
            _product.AddSubProduct(_nested);

            _key = NewKey(_product.Id);
            _nestedKey = NewKey(_nested.Id);
            _otherKey = NewKey(_otherProduct.Id);
            _masterKey = NewKey(Guid.Empty);

            foreach (var product in new[] { _product, _nested, _otherProduct })
                _cache.Setup(c => c.TryGetProduct(product.Id, out It.Ref<ProductModel>.IsAny))
                    .Callback(new OutProductCallback((Guid _, out ProductModel p) => p = product))
                    .Returns(true);

            foreach (var key in new[] { _key, _nestedKey, _otherKey, _masterKey })
                _cache.Setup(c => c.GetAccessKey(key.Id)).Returns(key);

            _cache.Setup(c => c.GetProducts()).Returns(new List<ProductModel> { _product, _otherProduct });
            _userManager.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns(new List<User>());

            _tree = new TreeViewModel(_cache.Object, _folderManager.Object, _userManager.Object);
        }


        private static ProductModel NewProduct(string name) =>
            new(EntitiesFactory.BuildProductEntity(name: name) with { Id = Guid.NewGuid().ToString() });

        private static AccessKeyModel NewKey(Guid productId) =>
            new(EntitiesFactory.BuildAccessKeyEntity(name: "key", productId: productId.ToString(), permissions: KeyPermissions.CanReadSensorData));

        private static User Admin() => new("admin") { IsAdmin = true };

        private static User UserWith(Guid productId, ProductRoleEnum role) =>
            new("someone") { ProductsRoles = { (productId, role) } };

        // The form as the page posts it: the key's id, the id of the product the form was opened for,
        // a new name and the full set of permissions.
        private static EditAccessKeyViewModel Form(Guid keyId, Guid postedProductId) =>
            new()
            {
                Id = keyId,
                SelectedProductId = postedProductId,
                DisplayName = NewName,
                CanSendSensorData = true,
                CanAddNodes = true,
                CanAddSensors = true,
                CanReadSensorData = true,
            };

        private AccessKeysController CreateController(User user) =>
            new(_cache.Object, _userManager.Object, _tree)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = user },
                },
            };

        private IActionResult Post(User user, EditAccessKeyViewModel form, bool invalidModel = false)
        {
            var controller = CreateController(user);

            if (invalidModel)
                controller.ModelState.AddModelError(nameof(EditAccessKeyViewModel.DisplayName), "invalid");

            return DeclaredFilters.Refusal(controller, nameof(AccessKeysController.ModifyAccessKey), form)
                ?? controller.ModifyAccessKey(form);
        }

        private static int StatusCodeOf(IActionResult result) =>
            result switch
            {
                StatusCodeResult codeResult => codeResult.StatusCode,
                ObjectResult objectResult => objectResult.StatusCode ?? 200,
                _ => throw new InvalidOperationException($"unexpected result type {result.GetType().Name}"),
            };

        private void VerifyNothingUpdated() =>
            _cache.Verify(c => c.UpdateAccessKey(It.IsAny<AccessKeyUpdate>()), Times.Never);

        private void VerifyUpdated(AccessKeyModel key) =>
            _cache.Verify(c => c.UpdateAccessKey(It.Is<AccessKeyUpdate>(u => u.Id == key.Id && u.DisplayName == NewName)), Times.Once);


        [Fact]
        public void Admin_UpdatesAKeyOfAnyProduct()
        {
            var result = Post(Admin(), Form(_otherKey.Id, _otherProduct.Id));

            Assert.IsType<PartialViewResult>(result);
            VerifyUpdated(_otherKey);
        }

        [Fact]
        public void Admin_UpdatesAMasterKey()
        {
            var result = Post(Admin(), Form(_masterKey.Id, Guid.Empty));

            Assert.IsType<PartialViewResult>(result);
            VerifyUpdated(_masterKey);
        }

        [Fact]
        public void ManagerOfTheKeysProduct_UpdatesTheKey()
        {
            var result = Post(UserWith(_product.Id, ProductRoleEnum.ProductManager), Form(_key.Id, _product.Id));

            Assert.IsType<PartialViewResult>(result);
            VerifyUpdated(_key);
        }

        [Fact]
        public void ManagerOfTheRoot_UpdatesAKeyOfANestedNode()
        {
            // Roles are held per root product; a key issued for a node below it is the root Manager's.
            var result = Post(UserWith(_product.Id, ProductRoleEnum.ProductManager), Form(_nestedKey.Id, _nested.Id));

            Assert.IsType<PartialViewResult>(result);
            VerifyUpdated(_nestedKey);
        }

        [Fact]
        public void ManagerOfAnotherProduct_PostingTheirOwnProductId_Gets403_AndNothingIsUpdated()
        {
            // The request that used to pass: the product id is the caller's own, the key id is not.
            var result = Post(UserWith(_product.Id, ProductRoleEnum.ProductManager), Form(_otherKey.Id, _product.Id));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingUpdated();
        }

        [Fact]
        public void ManagerOfAnotherProduct_PostingTheKeysProductId_IsRefused_AndNothingIsUpdated()
        {
            var result = Post(UserWith(_product.Id, ProductRoleEnum.ProductManager), Form(_otherKey.Id, _otherProduct.Id));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingUpdated();
        }

        [Fact]
        public void ManagerOfAProduct_Gets403OnAMasterKey_AndNothingIsUpdated()
        {
            // A master key belongs to no product: it is the admins' alone, whatever product id is posted.
            var result = Post(UserWith(_product.Id, ProductRoleEnum.ProductManager), Form(_masterKey.Id, _product.Id));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingUpdated();
        }

        [Fact]
        public void Viewer_Gets403_AndNothingIsUpdated()
        {
            var result = Post(UserWith(_product.Id, ProductRoleEnum.ProductViewer), Form(_key.Id, _product.Id));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingUpdated();
        }

        [Fact]
        public void UserWithoutAnyRole_Gets403_AndNothingIsUpdated()
        {
            var result = Post(new User("stranger"), Form(_key.Id, _product.Id));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingUpdated();
        }

        [Fact]
        public void RoleOnTheNestedNodesOwnId_CountsForNothing()
        {
            // A role entry stored under a nested node's id is stale data: only the root's entry counts.
            var result = Post(UserWith(_nested.Id, ProductRoleEnum.ProductManager), Form(_nestedKey.Id, _nested.Id));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingUpdated();
        }

        [Fact]
        public void UnknownKey_Is404_AndNothingIsUpdated()
        {
            var result = Post(Admin(), Form(Guid.NewGuid(), _product.Id));

            Assert.Equal(StatusCodes.Status404NotFound, StatusCodeOf(result));
            VerifyNothingUpdated();
        }

        [Fact]
        public void UnknownKey_Is404_ForAManagerToo()
        {
            var result = Post(UserWith(_product.Id, ProductRoleEnum.ProductManager), Form(Guid.NewGuid(), _product.Id));

            Assert.Equal(StatusCodes.Status404NotFound, StatusCodeOf(result));
            VerifyNothingUpdated();
        }

        [Fact]
        public void NameValidation_LeavesAnUnknownKeyToTheAction()
        {
            // The name check runs while the form is bound, before the action. It reads the stored key,
            // and for an id that is not stored it must pass, not throw: the 404 is the action's to give.
            var services = new Mock<IServiceProvider>();
            services.Setup(s => s.GetService(typeof(ITreeValuesCache))).Returns(_cache.Object);
            services.Setup(s => s.GetService(typeof(IFolderManager))).Returns(_folderManager.Object);
            var form = Form(Guid.NewGuid(), _product.Id);

            var result = new UniqueValidationAttribute().GetValidationResult(form.DisplayName, new ValidationContext(form, services.Object, null));

            Assert.Equal(ValidationResult.Success, result);
        }

        [Fact]
        public void KeyOfAProductThatIsGone_IsTheAdminsOnly()
        {
            var orphan = NewKey(Guid.NewGuid());
            _cache.Setup(c => c.GetAccessKey(orphan.Id)).Returns(orphan);

            var refused = Post(UserWith(_product.Id, ProductRoleEnum.ProductManager), Form(orphan.Id, _product.Id));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(refused));
            VerifyNothingUpdated();

            Post(Admin(), Form(orphan.Id, orphan.ProductId));

            VerifyUpdated(orphan);
        }

        [Fact]
        public void RefusedCaller_Gets403_EvenWhenTheFormIsInvalid()
        {
            // The check comes before the form is looked at: an invalid form must not turn the refusal
            // into the edit dialog of someone else's key.
            var result = Post(UserWith(_product.Id, ProductRoleEnum.ProductManager), Form(_otherKey.Id, _product.Id), invalidModel: true);

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingUpdated();
        }

        [Fact]
        public void Answer_ListsTheKeysOfTheStoredKeysProduct_NotOfThePostedOne()
        {
            // The answer is the key list of a product, and key ids are the keys themselves. It must be
            // the list of the product the edited key belongs to, whatever product id the form carried.
            var result = Post(UserWith(_product.Id, ProductRoleEnum.ProductManager), Form(_key.Id, _otherProduct.Id));

            var view = Assert.IsType<PartialViewResult>(result);
            Assert.Same(_tree.Nodes[_product.Id], view.Model);
            VerifyUpdated(_key);
        }

        [Fact]
        public void InvalidForm_ReopensTheDialogForTheStoredKeysProduct()
        {
            var form = Form(_key.Id, Guid.Empty);

            var result = Post(UserWith(_product.Id, ProductRoleEnum.ProductManager), form, invalidModel: true);

            var view = Assert.IsType<PartialViewResult>(result);
            var model = Assert.IsType<EditAccessKeyViewModel>(view.Model);
            Assert.False(model.IsMaster);
            Assert.Equal(_product.Id, Assert.Single(model.Products).Id);
            VerifyNothingUpdated();
        }
    }
}
