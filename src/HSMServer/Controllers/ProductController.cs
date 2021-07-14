using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.DataLayer.Model;
using HSMServer.Model.Validators;
using HSMServer.Model.ViewModel;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using HSMServer.Products;
using HSMServer.Keys;
using System;
using HSMServer.Configuration;
using System.Security.Cryptography;
using HSMServer.Filters;

namespace HSMServer.Controllers
{
    [Authorize]
    public class ProductController : Controller
    {
        private readonly IMonitoringCore _monitoringCore;
        private readonly IUserManager _userManager;
        private readonly IProductManager _productManager;
        private readonly IConfigurationProvider _configurationProvider;

        public ProductController(IMonitoringCore monitoringCore, IUserManager userManager,
            IProductManager productManager, IConfigurationProvider configurationProvider)
        {
            _monitoringCore = monitoringCore;
            _userManager = userManager;
            _productManager = productManager;
            _configurationProvider = configurationProvider;
        }

        public IActionResult Index()
        {
            var user = HttpContext.User as User;

            List<Product> products = null;
            if (UserRoleHelper.IsProductCRUDAllowed(user.IsAdmin))
                products = _monitoringCore.GetAllProducts();
            else
                products = _monitoringCore.GetProducts(user);

            products = products?.OrderBy(x => x.Name).ToList();

            var result = products?.Select(x => new ProductViewModel(
                _userManager.GetManagers(x.Key).FirstOrDefault()?.UserName ?? "---", x)).ToList();

            return View(result);
        }

        [ProductRoleFilter(ProductRoleEnum.ProductManager)]
        public IActionResult EditProduct([FromQuery(Name = "Product")] string productKey)
        {
            var product = _monitoringCore.GetProduct(productKey);
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

        public void CreateProduct([FromQuery(Name = "Product")] string productName)
        {
            Product product = new Product();
            product.Name = productName;

            ProductValidator validator = new ProductValidator(_monitoringCore);
            var results = validator.Validate(product);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return;
            }

            TempData.Remove(TextConstants.TempDataErrorText);
            _monitoringCore.AddProduct(HttpContext.User as User, productName,
                out Product newProduct, out string error);
        }

        public void RemoveProduct([FromQuery(Name = "Product")] string productKey)
        {
            _monitoringCore.RemoveProduct(productKey, out string error);
        }


        [HttpPost]
        public void UpdateProduct([FromBody] ProductViewModel model)
        {
            model.Name = model.Name.Replace('-', ' ');

            Product product = GetModelFromViewModel(model);
            _monitoringCore.UpdateProduct(HttpContext.User as User, product);
        }

        [HttpPost]
        public void AddExtraKey([FromBody] ExtraKeyViewModel model)
        {
            ExtraKeyValidator validator = new ExtraKeyValidator(_monitoringCore);
            var results = validator.Validate(model);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataKeyErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return;
            }

            Product product = _monitoringCore.GetProduct(model.ProductKey);
            model.ExtraProductKey = KeyGenerator.GenerateExtraProductKey(
                product.Name, model.ExtraKeyName);

            var extraProduct = new ExtraProductKey(model.ExtraKeyName, model.ExtraProductKey);
            if (product.ExtraKeys == null || product.ExtraKeys.Count == 0)
                product.ExtraKeys = new List<ExtraProductKey> { extraProduct };
            else
                product.ExtraKeys.Add(extraProduct);

            _monitoringCore.UpdateProduct(HttpContext.User as User, product);
        }

        [HttpPost]
        public void RemoveExtraKey([FromBody] ExtraKeyViewModel model)
        {
            Product product = _monitoringCore.GetProduct(model.ProductKey);
            product.ExtraKeys.Remove(product.ExtraKeys.First(x => x.Key.Equals(model.ExtraProductKey)));

            _monitoringCore.UpdateProduct(HttpContext.User as User, product);
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
            if (string.IsNullOrEmpty(role.Key) && role.Value == (ProductRoleEnum) 0)
                return;

            user.ProductsRoles.Remove(role);

            if (user.ProductsRoles == null || !user.ProductsRoles.Any())
                user.ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>> { pair };
            else
                user.ProductsRoles.Add(pair);

            _userManager.UpdateUser(user);

        }

        [HttpPost]
        public void Invite([FromBody] InviteViewModel model)
        {
            InviteValidator validator = new InviteValidator();
            var results = validator.Validate(model);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataInviteErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return;
            }

            var str = $"{model.ProductKey}_{model.Role}_{DateTime.UtcNow + TimeSpan.FromMinutes(30)}";

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
            
            var (cipher, nonce, tag) = AESCypher.Encrypt(str, keyBytes);

            var link = $"{Request.Scheme}://{Request.Host}/" +
                $"{ViewConstants.AccountController}/{ViewConstants.RegistrationAction}" +
                $"?Cipher={cipher}&Tag={tag}&Nonce={nonce}";

            var server = _configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.SMTPServer).Value;
            var port = _configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.SMTPPort).Value;
            var login = _configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.SMTPLogin).Value;
            var password = _configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.SMTPPassword).Value;
            var fromEmail = _configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.SMTPFromEmail).Value;

            EmailSender sender = new EmailSender(server, 
                string.IsNullOrEmpty(port) ? null : Int32.Parse(port),
                login, password, fromEmail, model.Email);

            sender.Send("Invitation link HSM", link);
        }

        private Product GetModelFromViewModel(ProductViewModel productViewModel)
        {
            Product existingProduct = _monitoringCore.GetProduct(productViewModel.Key);

            Product product = new Product()
            {
                DateAdded = productViewModel.CreationDate,
                Name = productViewModel.Name,
                Key = productViewModel.Key,
                ExtraKeys = existingProduct.ExtraKeys
            };

            return product;
        }
    }
}
