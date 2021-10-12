using HSMCommon.Constants;
using HSMServer.Constants;
using HSMServer.Core.Authentication;
using HSMServer.Core.Configuration;
using HSMServer.Core.Email;
using HSMServer.Core.Encryption;
using HSMServer.Core.Helpers;
using HSMServer.Core.Keys;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.MonitoringServerCore;
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
using HSMServer.Core.MonitoringCoreInterface;

namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class ProductController : Controller
    {
        private readonly IProductsInterface _productsInterface;
        private readonly IUserManager _userManager;
        private readonly IConfigurationProvider _configurationProvider;
        private readonly IRegistrationTicketManager _ticketManager;
        private readonly ILogger<ProductController> _logger;

        public ProductController(IProductsInterface productsInterface, IUserManager userManager,
            IConfigurationProvider configurationProvider, IRegistrationTicketManager ticketManager, ILogger<ProductController> logger)
        {
            _productsInterface = productsInterface;
            _userManager = userManager;
            _ticketManager = ticketManager;
            _configurationProvider = configurationProvider;
            _logger = logger;
        }

        #region Products
        public IActionResult Index()
        {
            var user = HttpContext.User as User;

            List<Product> products = null;
            if (UserRoleHelper.IsProductCRUDAllowed(user))
                products = _productsInterface.GetAllProducts();
            else
                products = _productsInterface.GetProducts(user);

            products = products?.OrderBy(x => x.Name).ToList();

            var result = products?.Select(x => new ProductViewModel(
                _userManager.GetManagers(x.Key).FirstOrDefault()?.UserName ?? "---", x)).ToList();

            return View(result);
        }

        public void CreateProduct([FromQuery(Name = "Product")] string productName)
        {
            NewProductNameValidator validator = new NewProductNameValidator(_productsInterface);
            var results = validator.Validate(productName);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return;
            }

            TempData.Remove(TextConstants.TempDataErrorText);
            _productsInterface.AddProduct(HttpContext.User as User, productName,
                out Product newProduct, out string error);
        }

        public void RemoveProduct([FromQuery(Name = "Product")] string productKey)
        {
            _productsInterface.RemoveProduct(productKey, out string error);
        }

        [HttpPost]
        public void UpdateProduct([FromBody] ProductViewModel model)
        {
            Product product = GetModelFromViewModel(model);
            _productsInterface.UpdateProduct(HttpContext.User as User, product);
        }

        #endregion

        #region Edit Product

        [ProductRoleFilter(ProductRoleEnum.ProductManager)]
        public IActionResult EditProduct([FromQuery(Name = "Product")] string productKey)
        {
            var product = _productsInterface.GetProduct(productKey);
            var users = _userManager.GetViewers(productKey);

            var pairs = new List<KeyValuePair<User, ProductRoleEnum>>();
            if (users != null || users.Any())
                foreach (var user in users.OrderBy(x => x.UserName))
                {
                    pairs.Add(new KeyValuePair<User, ProductRoleEnum>(user,
                        user.ProductsRoles.First(x => x.Key.Equals(product.Key)).Value));
                }

            return View(new EditProductViewModel(product, pairs));
        }

        [HttpPost]
        public void AddExtraKey([FromBody] ExtraKeyViewModel model)
        {
            ExtraKeyValidator validator = new ExtraKeyValidator(_productsInterface);
            var results = validator.Validate(model);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataKeyErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return;
            }

            Product product = _productsInterface.GetProduct(model.ProductKey);
            model.ExtraProductKey = KeyGenerator.GenerateExtraProductKey(
                product.Name, model.ExtraKeyName);

            var extraProduct = new ExtraProductKey(model.ExtraKeyName, model.ExtraProductKey);
            if (product.ExtraKeys == null || product.ExtraKeys.Count == 0)
                product.ExtraKeys = new List<ExtraProductKey> { extraProduct };
            else
                product.ExtraKeys.Add(extraProduct);

            _productsInterface.UpdateProduct(HttpContext.User as User, product);
        }

        [HttpPost]
        public void RemoveExtraKey([FromBody] ExtraKeyViewModel model)
        {
            Product product = _productsInterface.GetProduct(model.ProductKey);
            product.ExtraKeys.Remove(product.ExtraKeys.First(x => x.Key.Equals(model.ExtraProductKey)));

            _productsInterface.UpdateProduct(HttpContext.User as User, product);
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
            if (string.IsNullOrEmpty(role.Key) && role.Value == (ProductRoleEnum)0)
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
                string.IsNullOrEmpty(port) ? null : Int32.Parse(port),
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

        private Product GetModelFromViewModel(ProductViewModel productViewModel)
        {
            Product existingProduct = _productsInterface.GetProduct(productViewModel.Key);

            Product product = new Product(productViewModel.Key, productViewModel.Name, productViewModel.CreationDate)
            {
                ExtraKeys = existingProduct.ExtraKeys
            };

            return product;
        }
    }
}
