using System;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Controllers
{
    // The key list of one product (the "Access keys" entry of the tree) is served for a product the user
    // sees: any role on its root product, or admin. Everyone else gets the answer of an unknown id.
    public class AccessKeysControllerAccessTests
    {
        [Fact]
        public void KeyList_IsServedToTheProductsUsers_AndToAnAdmin()
        {
            var tree = new WebAccessFixture();

            foreach (var user in new[] { tree.Member, tree.Manager, tree.Admin })
            {
                var list = Assert.IsType<PartialViewResult>(tree.AccessKeys(user).AccessKeysForProduct(tree.Own.Id.ToString()));
                var product = Assert.IsType<ProductNodeViewModel>(list.Model);

                Assert.Equal("_ProductAccessKeys", list.ViewName);
                Assert.Equal(tree.Own.Id, product.Id);
                Assert.NotEmpty(product.GetAccessKeys());
            }
        }

        [Fact]
        public void KeyList_OfAProductTheUserHasNoRoleOn_IsAnsweredLikeAnUnknownId()
        {
            var tree = new WebAccessFixture();
            var keys = tree.AccessKeys(tree.Outsider);

            var refused = WebAccessFixture.Describe(keys.AccessKeysForProduct(tree.Own.Id.ToString()));
            var unknown = WebAccessFixture.Describe(keys.AccessKeysForProduct(Guid.NewGuid().ToString()));

            Assert.Equal("404", refused);
            Assert.Equal(unknown, refused);
            Assert.Equal(unknown, WebAccessFixture.Describe(keys.AccessKeysForProduct("not an id")));
            Assert.Equal(unknown, WebAccessFixture.Describe(keys.AccessKeysForProduct(null)));
        }

        [Fact]
        public void KeyList_OfANestedNode_FollowsTheRootProduct()
        {
            // Roles are held per root product: a role entry on the nested node's own id counts for nothing.
            var tree = new WebAccessFixture();
            var module = tree.ServiceAlive.Parent.Id;

            var onNodeOnly = new User("role on the node id");
            onNodeOnly.ProductsRoles.Add((module, ProductRoleEnum.ProductManager));

            Assert.IsType<PartialViewResult>(tree.AccessKeys(tree.Member).AccessKeysForProduct(module.ToString()));
            Assert.IsType<NotFoundResult>(tree.AccessKeys(tree.Outsider).AccessKeysForProduct(module.ToString()));
            Assert.IsType<NotFoundResult>(tree.AccessKeys(onNodeOnly).AccessKeysForProduct(module.ToString()));
        }

        [Fact]
        public void KeyList_StopsWhenTheRoleIsGone()
        {
            var tree = new WebAccessFixture();
            var keys = tree.AccessKeys(tree.Member);

            Assert.IsType<PartialViewResult>(keys.AccessKeysForProduct(tree.Own.Id.ToString()));

            tree.Member.ProductsRoles.Clear();

            Assert.IsType<NotFoundResult>(keys.AccessKeysForProduct(tree.Own.Id.ToString()));
        }
    }
}
