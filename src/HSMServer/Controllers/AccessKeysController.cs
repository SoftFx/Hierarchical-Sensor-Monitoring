using HSMServer.Core.Cache;
using HSMServer.Core.Model.Authentication;
using HSMServer.Filters.ProductRoleFilters;
using HSMServer.Helpers;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.TreeViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Controllers
{
    [Authorize]
    public class AccessKeysController : Controller
    {
        private readonly TreeViewModel _treeViewModel;

        internal ITreeValuesCache TreeValuesCache { get; }


        public AccessKeysController(ITreeValuesCache treeValuesCache, TreeViewModel treeViewModel)
        {
            TreeValuesCache = treeValuesCache;
            _treeViewModel = treeViewModel;
        }


        public IActionResult Index() => View(GetAvailableAccessKeys(isAllProducts: false));

        [HttpGet]
        public IActionResult AvailableAccessKeys([FromQuery(Name = "AllProducts")] bool isAllProducts) =>
            GetPartialAllAccessKeys(isAllProducts);

        [HttpGet]
        public IActionResult AccessKeysForProduct([FromQuery(Name = "Selected")] string productId) =>
            GetPartialProductAccessKeys(productId);

        [HttpGet]
        [ProductRoleFilterByEncodedProductId(ProductRoleEnum.ProductManager)]
        public IActionResult NewAccessKey([FromQuery(Name = "Selected")] string encodedProductId,
                                          [FromQuery(Name = "CloseModal")] bool closeModal = false) =>
            GetPartialNewAccessKey(
                new EditAccessKeyViewModel()
                {
                    EncodedProductId = encodedProductId,
                    CloseModal = closeModal,
                });

        [HttpPost]
        [ProductRoleFilterByKey(ProductRoleEnum.ProductManager)]
        public IActionResult NewAccessKey(EditAccessKeyViewModel key)
        {
            if (!ModelState.IsValid)
                return GetPartialNewAccessKey(key);

            TreeValuesCache.AddAccessKey(key.ToModel((HttpContext.User as User).Id));

            return GetPartialProductAccessKeys(key.EncodedProductId);
        }

        [HttpGet]
        [ProductRoleFilterBySelectedKey(ProductRoleEnum.ProductManager)]
        public IActionResult ModifyAccessKey([FromQuery(Name = "SelectedKey")] string selectedKey)
        {
            var key = TreeValuesCache.GetAccessKey(Guid.Parse(selectedKey));

            return GetPartialNewAccessKey(
                new EditAccessKeyViewModel(key)
                {
                    EncodedProductId = SensorPathHelper.Encode(key.ProductId),
                    CloseModal = true,
                    IsModify = true,
                });
        }

        [HttpPost]
        [ProductRoleFilterByKey(ProductRoleEnum.ProductManager)]
        public IActionResult ModifyAccessKey(EditAccessKeyViewModel key)
        {
            if (!ModelState.IsValid)
                return GetPartialNewAccessKey(key);

            TreeValuesCache.UpdateAccessKey(key.ToAccessKeyUpdate());

            return GetPartialNewAccessKey(key);
        }

        [HttpPost]
        [ProductRoleFilterBySelectedKey(ProductRoleEnum.ProductManager)]
        public IActionResult RemoveAccessKeyFromAllTable([FromQuery(Name = "SelectedKey")] string selectedKey,
                                                         [FromQuery(Name = "AllProducts")] bool isAllProducts)
        {
            TreeValuesCache.RemoveAccessKey(Guid.Parse(selectedKey));

            return GetPartialAllAccessKeys(isAllProducts);
        }

        [HttpPost]
        [ProductRoleFilterBySelectedKey(ProductRoleEnum.ProductManager)]
        public IActionResult RemoveAccessKeyFromProductTable([FromQuery(Name = "SelectedKey")] string selectedKey)
        {
            var accessKeyId = Guid.Parse(selectedKey);

            _treeViewModel.AccessKeys.TryGetValue(accessKeyId, out var key);
            _treeViewModel.Nodes.TryGetValue(key.ParentProduct.Id, out var productNode);

            TreeValuesCache.RemoveAccessKey(accessKeyId);

            return PartialView("_AllAccessKeys", productNode.GetAccessKeys());
        }


        private PartialViewResult GetPartialAllAccessKeys(bool isAllProducts) =>
            PartialView("_AllAccessKeys", GetAvailableAccessKeys(isAllProducts));

        private List<AccessKeyViewModel> GetAvailableAccessKeys(bool isAllProducts)
        {
            var user = HttpContext.User as User;
            var keys = new List<AccessKeyViewModel>(1 << 5);

            _treeViewModel.UpdateAccessKeysCharacteristics(user);

            var availableProducts = TreeValuesCache.GetProducts(user, isAllProducts);
            foreach (var product in availableProducts)
            {
                if (_treeViewModel.Nodes.TryGetValue(product.Id, out var productViewModel))
                    keys.AddRange(productViewModel.GetAccessKeys());
            }

            return keys.OrderBy(key => key?.NodePath).ToList();
        }

        private PartialViewResult GetPartialProductAccessKeys(string productId)
        {
            _treeViewModel.Nodes.TryGetValue(SensorPathHelper.Decode(productId), out var node);

            return PartialView("_ProductAccessKeys", node);
        }

        private PartialViewResult GetPartialNewAccessKey(EditAccessKeyViewModel key) =>
            PartialView("_NewAccessKey", key);
    }
}
