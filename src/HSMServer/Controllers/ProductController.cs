using System;
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
            if (UserRoleHelper.IsProductCRUDAllowed(user.Role))
                products = _monitoringCore.GetAllProducts();
            else
                products = _monitoringCore.GetProducts(user);

            products = products.OrderBy(x => x.Name).ToList();
            var result = products.Select(x => new ProductViewModel(x,
                _userManager.GetViewers(x.Key).Select(x => x.UserName)?.ToList()))?.ToList();

            return View(result);
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

        public void RemoveProduct([FromQuery(Name = "Product")] string productName)
        {
            productName = productName.Replace('-', ' ');

            _monitoringCore.RemoveProduct(HttpContext.User as User, productName,
                out Product product, out string error);
        }


        [HttpPost]
        public void UpdateProduct([FromBody] ProductViewModel productViewModel)
        {
            productViewModel.Name = productViewModel.Name.Replace('-', ' ');

            Product product = GetModelFromViewModel(productViewModel);
            _monitoringCore.UpdateProduct(HttpContext.User as User, product);
        }

        [HttpPost]
        public void AddExtraKeyToProduct([FromBody] ExtraKeyViewModel extraKeyViewModel)
        {
            ExtraKeyValidator validator = new ExtraKeyValidator(_monitoringCore);
            var results = validator.Validate(extraKeyViewModel);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return;
            }

            Product product = _monitoringCore.GetProduct(extraKeyViewModel.ProductKey);
            extraKeyViewModel.ExtraProductKey = KeyGenerator.GenerateExtraProductKey(
                product.Name, extraKeyViewModel.ExtraKeyName);

            var extraProduct = new ExtraProductKey(extraKeyViewModel.ExtraKeyName, extraKeyViewModel.ExtraProductKey);
            if (product.ExtraKeys == null || product.ExtraKeys.Count == 0)
                product.ExtraKeys = new List<ExtraProductKey> { extraProduct };
            else 
                product.ExtraKeys.Add(extraProduct);

            _monitoringCore.UpdateProduct(HttpContext.User as User, product);
        }

        private Product GetModelFromViewModel(ProductViewModel productViewModel)
        {
            Product product = new Product()
            {
                DateAdded = productViewModel.CreationDate,
                Name = productViewModel.Name,
                Key = productViewModel.Key,
                ExtraKeys = productViewModel.ExtraProductKeys,
                //ManagerId = Guid.NewGuid() // TODO0000000000000000000000
            };

            return product;
        }
    }
}
