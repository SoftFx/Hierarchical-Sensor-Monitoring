using HSMCommon.Constants;
using HSMServer.Authentication;
using HSMServer.Configuration;
using HSMServer.Constants;
using HSMServer.Core.Cache;
using HSMServer.Core.Registration;
using HSMServer.Email;
using HSMServer.Encryption;
using HSMServer.Filters.ProductRoleFilters;
using HSMServer.Folders;
using HSMServer.Helpers;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.Validators;
using HSMServer.Model.ViewModel;
using HSMServer.Registration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class ProductController : Controller
    {
        private readonly IUserManager _userManager;
        private readonly IConfigurationProvider _configurationProvider;
        private readonly IRegistrationTicketManager _ticketManager;
        private readonly ITreeValuesCache _treeValuesCache;
        private readonly IFolderManager _folderManager;
        private readonly TreeViewModel _treeViewModel;
        private readonly ILogger<ProductController> _logger;

        public ProductController(IUserManager userManager, IConfigurationProvider configurationProvider,
            IRegistrationTicketManager ticketManager, ITreeValuesCache treeValuesCache, IFolderManager folderManager,
            TreeViewModel treeViewModel, ILogger<ProductController> logger)
        {
            _userManager = userManager;
            _ticketManager = ticketManager;
            _configurationProvider = configurationProvider;
            _treeValuesCache = treeValuesCache;
            _folderManager = folderManager;
            _treeViewModel = treeViewModel;
            _logger = logger;
        }

        #region Products

        public IActionResult Index()
        {
            var userProducts = _treeViewModel.GetUserProducts(HttpContext.User as User);
            var userFolders = _folderManager.GetUserFolders(HttpContext.User as User);

            var folderProducts = new Dictionary<Guid, List<ProductViewModel>>();
            var productsWithoutFolder = new List<ProductViewModel>();

            foreach (var product in userProducts)
            {
                var productViewModel = new ProductViewModel(product, _userManager);

                if (product.FolderId.HasValue)
                {
                    var folderId = product.FolderId.Value;

                    if (!folderProducts.ContainsKey(folderId))
                        folderProducts[folderId] = new();

                    folderProducts[folderId].Add(productViewModel);
                }
                else
                    productsWithoutFolder.Add(productViewModel);
            }

            var folders = new List<FolderViewModel>();
            foreach (var (folderId, products) in folderProducts)
                folders.Add(new FolderViewModel(_folderManager[folderId], products));

            foreach (var folder in userFolders)
                if (!folderProducts.ContainsKey(folder.Id))
                    folders.Add(new FolderViewModel(folder, new List<ProductViewModel>()));

            folders = folders.OrderBy(f => f.Name).ToList();
            folders.Add(new FolderViewModel(productsWithoutFolder));

            return View(folders);
        }

        [HttpPost]
        public IActionResult FilterFolderProducts(Guid? folderId, string productName = "", string productManager = "")
        {
            ViewBag.ProductName = productName;
            ViewBag.ProductManager = productManager;

            var userProducts = _treeViewModel.GetUserProducts(HttpContext.User as User);
            var folderProducts = new List<ProductViewModel>();

            foreach (var product in userProducts)
                if (product.FolderId == folderId)
                {
                    var productVM = new ProductViewModel(product, _userManager);

                    if ((string.IsNullOrEmpty(productName) || productVM.Name.Contains(productName, StringComparison.CurrentCultureIgnoreCase)) &&
                        (string.IsNullOrEmpty(productManager) || productVM.Managers.Any(m => m.Contains(productManager, StringComparison.CurrentCultureIgnoreCase))))
                        folderProducts.Add(productVM);
                }

            var folder = folderId.HasValue
                ? new FolderViewModel(_folderManager[folderId.Value], folderProducts)
                : new FolderViewModel(folderProducts);

            return PartialView("_ProductList", folder);
        }

        [HttpPost]
        public IActionResult CreateProduct(AddProductViewModel product)
        {
            if (ModelState.IsValid)
                _treeValuesCache.AddProduct(product.Name);

            return PartialView("_AddProduct", product);
        }

        public void RemoveProduct([FromQuery(Name = "Product")] Guid productId)
        {
            _treeValuesCache.RemoveProduct(productId);
        }

        public IActionResult MoveProduct(Guid productId, Guid? fromFolderId, Guid? toFolderId)
        {
            if (fromFolderId is not null && _folderManager.TryGetValue(fromFolderId.Value, out var fromFolder))
                fromFolder.Products.RemoveAll(p => p.Id == productId);

            if (toFolderId is not null)
            {
                if (_folderManager.TryGetValue(toFolderId.Value, out var toFolder) && _treeViewModel.Nodes.TryGetValue(productId, out var product))
                {
                    toFolder.Products.Add(product);
                    _treeValuesCache.AddProductFolder(productId, toFolderId.Value);
                }
            }
            else
                _treeValuesCache.RemoveProductFolder(productId);

            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region Edit Product

        [ProductRoleFilterByEncodedProductId(ProductRoleEnum.ProductManager)]
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

        [HttpPost]
        public void AddUserRight([FromBody] UserRightViewModel model)
        {
            UserRightValidator validator = new UserRightValidator();
            var results = validator.Validate(model);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataUserErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return;
            }

            var user = _userManager[model.UserId];
            var pair = (model.EntityId, (ProductRoleEnum)model.ProductRole);

            if (user.ProductsRoles == null || !user.ProductsRoles.Any())
                user.ProductsRoles = new List<(Guid, ProductRoleEnum)> { pair };
            else
                user.ProductsRoles.Add(pair);

            _userManager.UpdateUser(user);
        }

        [HttpPost]
        public void RemoveUserRole([FromBody] UserRightViewModel model)
        {
            var user = _userManager[model.UserId];

            var role = user.ProductsRoles.First(ur => ur.Item1.Equals(model.EntityId));
            user.ProductsRoles.Remove(role);

            foreach (var sensorId in _treeViewModel.GetNodeAllSensors(model.EntityId))
                user.Notifications.RemoveSensor(sensorId);

            _userManager.UpdateUser(user);
        }

        [HttpPost]
        public void EditUserRole([FromBody] UserRightViewModel model)
        {
            var user = _userManager[model.UserId];
            var pair = (model.EntityId, (ProductRoleEnum)model.ProductRole);

            var role = user.ProductsRoles.FirstOrDefault(ur => ur.Item1.Equals(model.EntityId));
            //Skip empty corresponding pair
            if (role.Item1 == Guid.Empty && role.Item2 == 0)
                return;

            user.ProductsRoles.Remove(role);

            if (user.ProductsRoles == null || !user.ProductsRoles.Any())
                user.ProductsRoles = new List<(Guid, ProductRoleEnum)> { pair };
            else
                user.ProductsRoles.Add(pair);

            _userManager.UpdateUser(user);
        }

        [HttpPost]
        public async void Invite([FromBody] InviteViewModel model)
        {
            InviteValidator validator = new InviteValidator();
            var results = await validator.ValidateAsync(model);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataInviteErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return;
            }

            var ticket = new RegistrationTicket()
            {
                ExpirationDate = DateTime.UtcNow + TimeSpan.FromMinutes(30),
                ProductKey = model.ProductKey,
                Role = model.Role
            };
            _ticketManager.AddTicket(ticket);

            var (server, port, login, password, fromEmail) = GetMailConfiguration();

            EmailSender sender = new EmailSender(server,
                string.IsNullOrEmpty(port) ? null : int.Parse(port),
                login, password, fromEmail, model.Email);

            var link = GetLink(ticket.Id.ToString());

            Task.Run(() => sender.Send("Invitation link HSM", link));
        }

        #endregion

        private (string, string, string, string, string) GetMailConfiguration()
        {
            var server = _configurationProvider.ReadOrDefault(ConfigurationConstants.SMTPServer).Value;
            var port = _configurationProvider.ReadOrDefault(ConfigurationConstants.SMTPPort).Value;
            var login = _configurationProvider.ReadOrDefault(ConfigurationConstants.SMTPLogin).Value;
            var password = _configurationProvider.ReadOrDefault(ConfigurationConstants.SMTPPassword).Value;
            var fromEmail = _configurationProvider.ReadOrDefault(ConfigurationConstants.SMTPFromEmail).Value;

            return (server, port, login, password, fromEmail);
        }

        private string GetLink(string id)
        {
            try
            {
                var key = _configurationProvider.ReadConfigurationObject(ConfigurationConstants.AesEncryptionKey);
                byte[] keyBytes;
                if (key == null)
                {
                    var bytes = new byte[32];
                    RandomNumberGenerator.Fill(bytes);

                    _configurationProvider.AddConfigurationObject(ConfigurationConstants.AesEncryptionKey,
                        AESCypher.ToString(bytes));
                    keyBytes = bytes;
                }
                else
                    keyBytes = AESCypher.ToBytes(key.Value);

                var (cipher, nonce, tag) = AESCypher.Encrypt(id, keyBytes);

                return $"{Request.Scheme}://{Request.Host}/" +
                       $"{ViewConstants.AccountController}/{ViewConstants.RegistrationAction}" +
                       $"?Cipher={cipher}&Tag={tag}&Nonce={nonce}";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to create invitation link.");
            }

            return string.Empty;
        }
    }
}