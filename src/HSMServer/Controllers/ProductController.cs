using FluentValidation.Results;
using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.DataLayer.Model;
using HSMServer.Model.Validators;
using HSMServer.Model.ViewModel;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Http;

namespace HSMServer.Controllers
{
    [Authorize]
    public class ProductController : Controller
    {
        private readonly IMonitoringCore _monitoringCore;

        public ProductController(IMonitoringCore monitoringCore)
        {
            _monitoringCore = monitoringCore;
        }

        public IActionResult Index()
        {
            var products = _monitoringCore.GetAllProducts();

            return View(products.Select(x => new ProductViewModel(x))?.ToList());
        }

        public void CreateProduct([FromQuery(Name = "Product")] string productName)
        {
            Product product = new Product();
            product.Name = productName;

            ProductValidator validator = new ProductValidator(_monitoringCore);
            var results = validator.Validate(product);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataErrorText] = GetErrorString(results.Errors);
                return;
            }

            TempData.Remove(TextConstants.TempDataErrorText);
            _monitoringCore.AddProduct(HttpContext.User as User, productName,
                out Product newProduct, out string error);
        }

        public void RemoveProduct([FromQuery(Name = "Product")] string productName)
        {
            _monitoringCore.RemoveProduct(HttpContext.User as User, productName,
                out Product product, out string error);
        }

        private string GetErrorString(List<ValidationFailure> errors)
        {
            StringBuilder result = new StringBuilder();

            foreach (var error in errors)
            {
                result.Append(error.ErrorMessage + "\n");
            }

            return result.ToString();
        }
    }
}
