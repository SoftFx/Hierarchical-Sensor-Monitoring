using HSMCommon.Constants;
using HSMServer.Constants;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.Email;
using HSMServer.Core.Encryption;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Registration;
using HSMServer.Filters;
using HSMServer.Model.Validators;
using HSMServer.Model.ViewModel;
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
        private readonly ILogger<ProductController> _logger;

        public ProductController(IUserManager userManager, IConfigurationProvider configurationProvider,
            IRegistrationTicketManager ticketManager, ITreeValuesCache treeValuesCache, ILogger<ProductController> logger)
        {
            _userManager = userManager;
            _ticketManager = ticketManager;
            _configurationProvider = configurationProvider;
            _treeValuesCache = treeValuesCache;
            _logger = logger;
        }

        #region Products
        public IActionResult Index()
        {
            var user = HttpContext.User as User;

            var products = _treeValuesCache.GetProductsWithoutParent(user);

            products = products?.OrderBy(x => x.DisplayName).ToList();

            var result = products?.Select(x => new ProductViewModel(
                _userManager.GetManagers(x.Id).FirstOrDefault()?.UserName ?? "---", x)).ToList();

            return View(result);
        }

        public void CreateProduct([FromQuery(Name = "Product")] string productName)
        {
            NewProductNameValidator validator = new NewProductNameValidator(_treeValuesCache);
            var results = validator.Validate(productName);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return;
            }

            TempData.Remove(TextConstants.TempDataErrorText);
            _treeValuesCache.AddProduct(productName);
        }

        public void RemoveProduct([FromQuery(Name = "Product")] string productId)
        {
            _treeValuesCache.RemoveProduct(productId);
        }

        #endregion

        #region Edit Product

        [ProductRoleFilter(ProductRoleEnum.ProductManager)]
        public IActionResult EditProduct([FromQuery(Name = "Product")] string productId)
        {
            var product = _treeValuesCache.GetProduct(productId);
            var users = _userManager.GetViewers(productId);

            var pairs = new List<KeyValuePair<User, ProductRoleEnum>>();
            if (users != null || users.Any())
                foreach (var user in users.OrderBy(x => x.UserName))
                {
                    pairs.Add(new KeyValuePair<User, ProductRoleEnum>(user,
                        user.ProductsRoles.First(x => x.Key.Equals(product.Id)).Value));
                }

            return View(new EditProductViewModel(product, pairs));
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

            var user = _userManager.GetUser(Guid.Parse(model.UserId));
            var pair = new KeyValuePair<string, ProductRoleEnum>(model.ProductKey, (ProductRoleEnum)model.ProductRole);

            if (user.ProductsRoles == null || !user.ProductsRoles.Any())
                user.ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>> { pair };
            else
                user.ProductsRoles.Add(pair);

            _userManager.UpdateUser(user);
        }

        [HttpPost]
        public void RemoveUserRole([FromBody] UserRightViewModel model)
        {
            var user = _userManager.GetUser(Guid.Parse(model.UserId));

            var role = user.ProductsRoles.First(ur => ur.Key.Equals(model.ProductKey));
            user.ProductsRoles.Remove(role);

            _userManager.UpdateUser(user);
        }

        [HttpPost]
        public void EditUserRole([FromBody] UserRightViewModel model)
        {
            var user = _userManager.GetUser(Guid.Parse(model.UserId));
            var pair = new KeyValuePair<string, ProductRoleEnum>(model.ProductKey, (ProductRoleEnum)model.ProductRole);

            var role = user.ProductsRoles.FirstOrDefault(ur => ur.Key.Equals(model.ProductKey));
            //Skip empty corresponding pair
            if (string.IsNullOrEmpty(role.Key) && role.Value == 0)
                return;

            user.ProductsRoles.Remove(role);

            if (user.ProductsRoles == null || !user.ProductsRoles.Any())
                user.ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>> { pair };
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
            var server = _configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.SMTPServer).Value;
            var port = _configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.SMTPPort).Value;
            var login = _configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.SMTPLogin).Value;
            var password = _configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.SMTPPassword).Value;
            var fromEmail = _configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.SMTPFromEmail).Value;

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
