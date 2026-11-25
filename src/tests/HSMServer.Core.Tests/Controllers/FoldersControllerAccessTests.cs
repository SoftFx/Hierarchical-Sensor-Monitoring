using System;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Controllers
{
    // The Users tab of the folder edit page lists every user of the server and the folder's roles. It is
    // served to the folder's Managers and to admins; everyone else gets the answer of an unknown id.
    public class FoldersControllerAccessTests
    {
        [Fact]
        public void UsersList_IsServedToTheFoldersManager_AndToAnAdmin()
        {
            var tree = new WebAccessFixture();

            foreach (var user in new[] { tree.Manager, tree.Admin })
            {
                var list = Assert.IsType<PartialViewResult>(tree.FolderPage(user).ResetUsers(tree.Folder.Id));

                var users = Assert.IsType<FolderUsersViewModel>(list.Model);

                Assert.Equal("_Users", list.ViewName);
                Assert.Equal(tree.Folder.Name, users.FolderName);

                // The users who can be added: everyone who is not an admin.
                Assert.Contains(tree.Member, users.NotAdminUsers);
                Assert.DoesNotContain(tree.Admin, users.NotAdminUsers);
            }
        }

        [Fact]
        public void UsersList_IsAnsweredLikeAnUnknownId_ToEveryoneElse()
        {
            var tree = new WebAccessFixture();

            // Sees the folder through its role, but does not manage it.
            var folderViewer = new User("folder viewer");
            folderViewer.FoldersRoles.Add(tree.Folder.Id, ProductRoleEnum.ProductViewer);

            // Manages a product of the folder, with no role on the folder.
            var productManager = new User("product manager");
            productManager.ProductsRoles.Add((tree.Own.Id, ProductRoleEnum.ProductManager));

            foreach (var user in new[] { tree.Outsider, tree.Member, folderViewer, productManager })
            {
                var page = tree.FolderPage(user);

                var refused = WebAccessFixture.Describe(page.ResetUsers(tree.Folder.Id));
                var unknown = WebAccessFixture.Describe(page.ResetUsers(Guid.NewGuid()));

                Assert.Equal("404", refused);
                Assert.Equal(unknown, refused);
            }
        }

        [Fact]
        public void UsersList_OfAnUnknownFolder_IsNotFoundForAnAdminToo()
        {
            var tree = new WebAccessFixture();

            Assert.IsType<NotFoundResult>(tree.FolderPage(tree.Admin).ResetUsers(Guid.NewGuid()));
        }
    }
}
