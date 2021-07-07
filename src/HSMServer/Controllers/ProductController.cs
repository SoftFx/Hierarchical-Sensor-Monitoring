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

namespace HSMServer.Controllers
{
    [Authorize]
    public class ProductController : Controller
    {
        private readonly IMonitoringCore _monitoringCore;
        private readonly IUserManager _userManager;
        private readonly IProductManager _productManager;

        public ProductController(IMonitoringCore monitoringCore, IUserManager userManager,
            IProductManager productManager)
        {
            _monitoringCore = monitoringCore;
            _userManager = userManager;
            _productManager = productManager;
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

        public IActionResult EditProduct([FromQuery(Name = "Product")] string productKey)
        {
            var product = _monitoringCore.GetProduct(productKey);
            var users = _userManager.GetViewers(productKey);

            var pairs = new List<KeyValuePair<User, ProductRoleEnum>>();
            if (users != null || !users.Any())
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

            var role = user.ProductsRoles.First(ur => ur.Key.Equals(model.ProductKey));
            user.ProductsRoles.Remove(role);

            if (user.ProductsRoles == null || !user.ProductsRoles.Any())
                user.ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>> { pair };
            else
                user.ProductsRoles.Add(pair);

            _userManager.UpdateUser(user);

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
