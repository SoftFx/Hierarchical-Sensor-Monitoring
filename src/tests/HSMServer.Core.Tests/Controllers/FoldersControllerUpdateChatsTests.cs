using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Controls;
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
    // /Folders/UpdateChats rebinds the alert chats and the default chats of a folder. It is the save
    // of the Chats tab on the folder edit page, which opens for the folder's Managers only, and it was
    // the one write of that page with no role filter: any signed-in user could post it for any folder
    // id. It must now require Manager of that folder (or admin). Invoked directly, as
    // ProductControllerTests does.
    public class FoldersControllerUpdateChatsTests
    {
        private delegate void OutFolderCallback(Guid id, out FolderModel folder);

        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folderManager = new();
        private readonly Mock<IUserManager> _userManager = new();
        private readonly Mock<IChatsManager> _chats = new();

        private readonly FolderModel _folder = new(new FolderEntity
        {
            Id = Guid.NewGuid().ToString(),
            DisplayName = "folder",
            AuthorId = Guid.NewGuid().ToString(),
            CreationDate = DateTime.UtcNow.Ticks,
        });

        private readonly Guid _chatId = Guid.NewGuid();


        public FoldersControllerUpdateChatsTests()
        {
            _cache.Setup(c => c.GetProducts()).Returns(new List<ProductModel>());
            _userManager.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns(new List<User>());
            _chats.Setup(c => c.GetValues()).Returns([]);

            _folderManager.Setup(f => f.TryGetValue(_folder.Id, out It.Ref<FolderModel>.IsAny))
                .Callback(new OutFolderCallback((Guid _, out FolderModel f) => f = _folder))
                .Returns(true);
            _folderManager.Setup(f => f[_folder.Id]).Returns(_folder);
            _folderManager.Setup(f => f.TryUpdate(It.IsAny<FolderUpdate>())).ReturnsAsync(true);
        }


        private FoldersController CreateController(User user) =>
            new(_folderManager.Object, _userManager.Object,
                new TreeViewModel(_cache.Object, _folderManager.Object, _userManager.Object), _chats.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = user },
                },
            };

        private User UserWith(ProductRoleEnum role)
        {
            var user = new User("someone");
            user.FoldersRoles.Add(_folder.Id, role);

            return user;
        }

        private FolderChatsViewModel Chats(Guid folderId) =>
            new()
            {
                FolderId = folderId,
                ConnectedChatIds = [_chatId],
                DefaultChats = new DefaultChatViewModel(),
            };

        private static int StatusCodeOf(IActionResult result) =>
            result switch
            {
                StatusCodeResult codeResult => codeResult.StatusCode,
                ObjectResult objectResult => objectResult.StatusCode ?? 200,
                _ => throw new InvalidOperationException($"unexpected result type {result.GetType().Name}"),
            };

        private void VerifyNothingUpdated() =>
            _folderManager.Verify(f => f.TryUpdate(It.IsAny<FolderUpdate>()), Times.Never);

        private void VerifyChatsUpdated() =>
            _folderManager.Verify(f => f.TryUpdate(It.Is<FolderUpdate>(u => u.Id == _folder.Id && u.Chats.SetEquals(new[] { _chatId }))), Times.Once);


        [Fact]
        public void UpdateChats_IsPostOnly_AndValidatesNoAntiforgeryToken()
        {
            var method = typeof(FoldersController).GetMethod(nameof(FoldersController.UpdateChats), BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(method);
            Assert.True(method.IsDefined(typeof(HttpPostAttribute), inherit: false));

            // Pinned on purpose: _Chats.cshtml replaces the content of the form with the saved
            // partial, which drops the token the form was rendered with, so with the attribute the
            // second save of a page would be a 400. Whoever adds it must keep the token in the form.
            Assert.False(method.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), inherit: false));
        }

        [Fact]
        public async Task Viewer_Gets403_AndNothingIsUpdated()
        {
            var result = await CreateController(UserWith(ProductRoleEnum.ProductViewer)).UpdateChats(Chats(_folder.Id));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingUpdated();
        }

        [Fact]
        public async Task UserWithoutAnyRole_Gets403_AndNothingIsUpdated()
        {
            var result = await CreateController(new User("stranger")).UpdateChats(Chats(_folder.Id));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingUpdated();
        }

        [Fact]
        public async Task ManagerOfAnotherFolder_Gets403_AndNothingIsUpdated()
        {
            var user = new User("neighbour");
            user.FoldersRoles.Add(Guid.NewGuid(), ProductRoleEnum.ProductManager);

            var result = await CreateController(user).UpdateChats(Chats(_folder.Id));

            Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
            VerifyNothingUpdated();
        }

        [Fact]
        public async Task UnknownFolder_Is404_AndNothingIsUpdated()
        {
            var result = await CreateController(new User("admin") { IsAdmin = true }).UpdateChats(Chats(Guid.NewGuid()));

            Assert.Equal(StatusCodes.Status404NotFound, StatusCodeOf(result));
            VerifyNothingUpdated();
        }

        [Fact]
        public async Task Manager_UpdatesTheChats()
        {
            var result = await CreateController(UserWith(ProductRoleEnum.ProductManager)).UpdateChats(Chats(_folder.Id));

            Assert.IsType<PartialViewResult>(result);
            VerifyChatsUpdated();
        }

        [Fact]
        public async Task Admin_UpdatesTheChats()
        {
            var result = await CreateController(new User("admin") { IsAdmin = true }).UpdateChats(Chats(_folder.Id));

            Assert.IsType<PartialViewResult>(result);
            VerifyChatsUpdated();
        }
    }
}
