using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Core.Cache;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Filters.ProductRoleFilters;
using HSMServer.Folders;
using HSMServer.Helpers;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.Validators;
using HSMServer.Model.ViewModel;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class ProductController : BaseController
    {
        private readonly IChatsManager _chatsManager;
        private readonly ITreeValuesCache _treeValuesCache;
        private readonly IFolderManager _folderManager;
        private readonly TreeViewModel _treeViewModel;
        private readonly ILogger<ProductController> _logger;


        public ProductController(IUserManager userManager, ITreeValuesCache treeValuesCache, IFolderManager folderManager,
            TreeViewModel treeViewModel, IChatsManager chatsManager, ILogger<ProductController> logger) : base(userManager)
        {
            _chatsManager = chatsManager;
            _treeValuesCache = treeValuesCache;
            _folderManager = folderManager;
            _treeViewModel = treeViewModel;
            _logger = logger;
        }

        #region Products

        public IActionResult Index()
        {
            var userProducts = _treeViewModel.GetUserProducts(CurrentUser);
            var userFolders = _folderManager.GetUserFolders(CurrentUser);

            var folderProducts = new Dictionary<Guid, List<ProductViewModel>>(1 << 2);
            var productsWithoutFolder = new List<ProductViewModel>(1 << 2);

            foreach (var product in userProducts)
            {
                var productViewModel = new ProductViewModel(product, _userManager);

                if (product.FolderId.HasValue)
                {
                    var folderId = product.FolderId.Value;

                    if (!folderProducts.ContainsKey(folderId))
                        folderProducts[folderId] = new(1 << 2);

                    folderProducts[folderId].Add(productViewModel);
                }
                else
                    productsWithoutFolder.Add(productViewModel);
            }

            var folders = new List<FolderViewModel>(folderProducts.Count);
            foreach (var (folderId, products) in folderProducts)
                folders.Add(new FolderViewModel(_folderManager[folderId], products));

            foreach (var folder in userFolders)
                if (!folderProducts.ContainsKey(folder.Id))
                    folders.Add(new FolderViewModel(folder, null));

            folders = folders.OrderBy(f => f.Name).AddFluent(new FolderViewModel(productsWithoutFolder));

            return View(folders);
        }

        [HttpPost]
        public IActionResult FilterFolderProducts(Guid? folderId, string productName = "", string productManager = "")
        {
            ViewBag.ProductName = productName;
            ViewBag.ProductManager = productManager;
            ViewBag.UserFolders = GetUserFolders();

            var userProducts = _treeViewModel.GetUserProducts(CurrentUser);
            var folderProducts = new List<ProductViewModel>(1 << 3);

            foreach (var product in userProducts)
                if (product.FolderId == folderId)
                {
                    var productVM = new ProductViewModel(product, _userManager);

                    if ((string.IsNullOrEmpty(productName) || productVM.Name.IgnoreCaseContains(productName)) &&
                        (string.IsNullOrEmpty(productManager) || productVM.Managers.Any(m => m.IgnoreCaseContains(productManager))))
                        folderProducts.Add(productVM);
                }

            var folder = folderId.HasValue
                ? new FolderViewModel(_folderManager[folderId.Value], folderProducts)
                : new FolderViewModel(folderProducts);

            return PartialView("_ProductList", folder);
        }

        [HttpPost]
        public async ValueTask<IActionResult> CreateProduct(AddProductViewModel product)
        {
            if (ModelState.IsValid)
                await _treeValuesCache.AddProductAsync(product.Name, CurrentUser.Id, CurrentInitiator);

            return PartialView("_AddProduct", product);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        // The RemoveProduct shape below, for the same reason: "Move to ..." is offered to the product's
        // Managers only, but the action checked no role and answered GET. A move is a change of roles —
        // FolderManager takes the product's roles from the users of the folder it leaves and gives the
        // users of the folder it enters their folder role on it — so anyone who held a role in a folder
        // could move any product there and receive that role on it.
        public async Task<IActionResult> MoveProduct(Guid productId, Guid? fromFolderId, Guid? toFolderId)
        {
            if (DenyUnlessManager(productId, out _) is { } denied)
                return denied;

            if (_treeViewModel.Nodes.TryGetValue(productId, out var product))
                await _folderManager.MoveProduct(product, fromFolderId, toFolderId, CurrentInitiator);

            return Ok();
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        // #1512: removing a product — or a nested node, since RemoveProductAsync is recursive and takes
        // any node id — wipes its subtree's history, policies, journal and access keys for good. Only
        // the button was hidden from non-managers: the action checked no role and answered GET, so any
        // signed-in Viewer, or a cross-site link riding the SameSite=Lax cookie, could delete any
        // product. Now: Manager of the node's root product (or admin), POST + antiforgery only — the
        // HomeController.RemoveNode and AlertTemplatesController.Remove shapes.
        public async Task<IActionResult> RemoveProduct(Guid product)
        {
            if (DenyUnlessManager(product, out var model) is { } denied)
                return denied;

            await _treeValuesCache.RemoveProductAsync(model.Id, CurrentInitiator);
            _logger.LogInformation("Product '{Path}' ({Id}) removed by '{User}'", model.FullPath, model.Id, CurrentUser.Name);

            return Ok();
        }

        #endregion

        #region Edit Product

        [ProductRoleFilterByEncodedProductId(nameof(encodedProductId), ProductRoleEnum.ProductManager)]
        public IActionResult EditProduct([FromQuery(Name = "Product")] string encodedProductId)
        {
            var notAdminUsers = _userManager.GetUsers(u => !u.IsAdmin).ToList();

            var decodedId = SensorPathHelper.DecodeGuid(encodedProductId);
            _treeViewModel.Nodes.TryGetValue(decodedId, out var productNode);

            var users = _userManager.GetViewers(decodedId);

            var pairs = new List<(User, ProductRoleEnum)>(1 << 6);

            var productNodeId = productNode?.Id;
            foreach (var user in users.OrderBy(x => x.Name))
            {
                pairs.Add((user, user.ProductsRoles.First(x => x.Item1.Equals(productNodeId)).Item2));
            }

            return View(new EditProductViewModel(productNode, pairs, notAdminUsers));
        }

        // The edit page (the GET above) is open to Managers only, but the four actions it posts to
        // checked no role: any signed-in user, a Viewer included, could post them for any product id
        // and rename the product, change its description and default chats, or hand out, change and
        // take away product roles (their own included). Now each of them requires what the page
        // requires, judged as RemoveProduct judges it: Manager of the id's root product, or admin.

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async ValueTask<IActionResult> EditProduct(ProductGeneralInfoViewModel viewModel)
        {
            if (DenyUnlessManager(viewModel.Id, out _) is { } denied)
                return denied;

            if (_treeViewModel.Nodes.TryGetValue(viewModel.Id, out var product) && ModelState.IsValid)
            {
                var result = await _treeValuesCache.UpdateProductAsync(viewModel.ToUpdate(product, _chatsManager, CurrentInitiator));

                if (!result.IsOk)
                    ModelState.AddModelError(nameof(ProductGeneralInfoViewModel.Name), result.Error);
            }
            else
            {
                viewModel.DefaultChats = new(product);
            }

            return PartialView("_EditProductGeneralInfo", viewModel);
        }

        [HttpPost]
        public IActionResult AddUserRight([FromBody] UserRightViewModel model)
        {
            if (DenyUnlessManager(model) is { } denied)
                return denied;

            var validator = new UserRightValidator();
            var results = validator.Validate(model);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataUserErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return Ok();
            }

            var user = _userManager[model.UserId];
            if (user is null)
                return NotFound();

            var pair = (model.EntityId, (ProductRoleEnum)model.ProductRole);

            if (user.ProductsRoles == null || user.ProductsRoles.Count == 0)
                user.ProductsRoles = [pair];
            else
                user.ProductsRoles.Add(pair);

            _userManager.UpdateUser(user);

            return Ok();
        }

        [HttpPost]
        public IActionResult RemoveUserRole([FromBody] UserRightViewModel model)
        {
            if (DenyUnlessManager(model) is { } denied)
                return denied;

            var user = _userManager[model.UserId];
            if (user is null)
                return NotFound();

            var role = user.ProductsRoles.First(ur => ur.Item1.Equals(model.EntityId));
            user.ProductsRoles.Remove(role);

            _userManager.UpdateUser(user);

            return Ok();
        }

        [HttpPost]
        public IActionResult EditUserRole([FromBody] UserRightViewModel model)
        {
            if (DenyUnlessManager(model) is { } denied)
                return denied;

            var user = _userManager[model.UserId];
            if (user is null)
                return NotFound();

            var pair = (model.EntityId, (ProductRoleEnum)model.ProductRole);

            var role = user.ProductsRoles.FirstOrDefault(ur => ur.Item1.Equals(model.EntityId));
            //Skip empty corresponding pair
            if (role.Item1 == Guid.Empty && role.Item2 == 0)
                return Ok();

            user.ProductsRoles ??= [];

            if (user.ProductsRoles.Count > 0)
                user.ProductsRoles.Remove(role);

            user.ProductsRoles.RemoveAll(x => x.Item1 == pair.Item1);
            user.ProductsRoles.Add(pair);

            _userManager.UpdateUser(user);

            return Ok();
        }

        //[HttpPost]
        //public async void Invite([FromBody] InviteViewModel model)
        //{
        //    InviteValidator validator = new InviteValidator();
        //    var results = await validator.ValidateAsync(model);
        //    if (!results.IsValid)
        //    {
        //        TempData[TextConstants.TempDataInviteErrorText] = ValidatorHelper.GetErrorString(results.Errors);
        //        return;
        //    }

        //    var ticket = new RegistrationTicket()
        //    {
        //        ExpirationDate = DateTime.UtcNow + TimeSpan.FromMinutes(30),
        //        ProductKey = model.ProductKey,
        //        Role = model.Role
        //    };
        //    _ticketManager.AddTicket(ticket);

        //    var (server, port, login, password, fromEmail) = GetMailConfiguration();

        //    EmailSender sender = new EmailSender(server,
        //        string.IsNullOrEmpty(port) ? null : int.Parse(port),
        //        login, password, fromEmail, model.Email);

        //    var link = GetLink(ticket.Id.ToString());

        //    Task.Run(() => sender.Send("Invitation link HSM", link));
        //}

        /// <summary>Update which sensor groups agents under this product should collect (#1198).</summary>
        [HttpPost]
        [ProductRoleFilterBySensorGroups(nameof(request), ProductRoleEnum.ProductManager)]
        public IActionResult UpdateSensorGroups([FromBody] SensorGroupsRequest request)
        {
            if (!_treeValuesCache.TryGetProduct(request.ProductId, out _))
                return NotFound("Product not found.");

            var result = _treeValuesCache.UpdateDisabledSensorGroups(request.ProductId, request.DisabledGroups ?? []);
            return result.IsOk ? Ok() : StatusCode(500, result.Error);
        }

        #endregion

        // The gate of every action that changes a product: null when the caller is an admin or a
        // Manager of the root product of nodeId, otherwise the answer to return: 404 for an id that is
        // not in the tree, 403 for everyone else. Roles are held per root product, so a node below the
        // root is judged by its root and a role entry on the node's own id counts for nothing.
        private IActionResult DenyUnlessManager(Guid nodeId, out ProductModel node)
        {
            if (!_treeValuesCache.TryGetProduct(nodeId, out node))
                return NotFound();

            return CurrentUser.IsManager(node.Root.Id) ? null : StatusCode(StatusCodes.Status403Forbidden);
        }

        // The same gate for the role actions, whose product id arrives in a JSON body. It runs before
        // the user is looked up, so a caller without the role learns nothing about which user ids exist.
        private IActionResult DenyUnlessManager(UserRightViewModel model) =>
            model is null ? BadRequest() : DenyUnlessManager(model.EntityId, out _);

        private Dictionary<string, string> GetUserFolders()
        {
            var userFolderIds = new HashSet<Guid>();

            var userProducts = _treeViewModel.GetUserProducts(CurrentUser);
            var userFolders = _folderManager.GetUserFolders(CurrentUser);

            foreach (var product in userProducts)
                if (product.FolderId.HasValue)
                    userFolderIds.Add(product.FolderId.Value);

            foreach (var folder in userFolders)
                userFolderIds.Add(folder.Id);


            var folders = new List<FolderViewModel>(userFolderIds.Count + 1);

            foreach (var folderId in userFolderIds)
                folders.Add(new FolderViewModel(_folderManager[folderId], null));

            folders = folders.OrderBy(f => f.Name).AddFluent(new FolderViewModel(null));

            return folders.ToDictionary(f => f.Id?.ToString() ?? string.Empty, f => f.Name);
        }

        //private (string, string, string, string, string) GetMailConfiguration()
        //{
        //    var server = _configurationProvider.ReadOrDefault(ConfigurationConstants.SMTPServer).Value;
        //    var port = _configurationProvider.ReadOrDefault(ConfigurationConstants.SMTPPort).Value;
        //    var login = _configurationProvider.ReadOrDefault(ConfigurationConstants.SMTPLogin).Value;
        //    var password = _configurationProvider.ReadOrDefault(ConfigurationConstants.SMTPPassword).Value;
        //    var fromEmail = _configurationProvider.ReadOrDefault(ConfigurationConstants.SMTPFromEmail).Value;

        //    return (server, port, login, password, fromEmail);
        //}

        //private string GetLink(string id)
        //{
        //    try
        //    {
        //        var key = _configurationProvider.ReadConfigurationObject(ConfigurationConstants.AesEncryptionKey);
        //        byte[] keyBytes;
        //        if (key == null)
        //        {
        //            var bytes = new byte[32];
        //            RandomNumberGenerator.Fill(bytes);

        //            _configurationProvider.AddConfigurationObject(ConfigurationConstants.AesEncryptionKey,
        //                AESCypher.ToString(bytes));
        //            keyBytes = bytes;
        //        }
        //        else
        //            keyBytes = AESCypher.ToBytes(key.Value);

        //        var (cipher, nonce, tag) = AESCypher.Encrypt(id, keyBytes);

        //        return $"{Request.Scheme}://{Request.Host}/" +
        //               $"{ViewConstants.AccountController}/{ViewConstants.RegistrationAction}" +
        //               $"?Cipher={cipher}&Tag={tag}&Nonce={nonce}";
        //    }
        //    catch (Exception e)
        //    {
        //        _logger.LogError(e, "Failed to create invitation link.");
        //    }

        //    return string.Empty;
        //}
    }
}