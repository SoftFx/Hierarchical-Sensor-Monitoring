using HSMServer.Core.Cache;
using HSMServer.Core.Model.Authentication;
using HSMServer.Helpers;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.TreeViewModels;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;

namespace HSMServer.Controllers
{
    public class AccessKeysController : Controller
    {
        private readonly ITreeValuesCache _treeValuesCache;
        private readonly TreeViewModel _treeViewModel;


        public AccessKeysController(ITreeValuesCache treeValuesCache, TreeViewModel treeViewModel)
        {
            _treeValuesCache = treeValuesCache;
            _treeViewModel = treeViewModel;
        }


        public IActionResult Index() => View(GetAvailableAccessKeys(onlyProductsWithoutParent: true));

        [HttpGet]
        public IActionResult AvailableAccessKeys([FromQuery(Name = "WithoutParents")] bool productsWithoutParent) =>
            GetPartialAllAccessKeys(productsWithoutParent);

        [HttpGet]
        public IActionResult AccessKeysForProduct([FromQuery(Name = "Selected")] string productId) =>
            GetPartialProductAccessKeys(productId);

        [HttpGet]
        public IActionResult NewAccessKey([FromQuery(Name = "Selected")] string productId) =>
            PartialView("_NewAccessKey", new EditAccessKeyViewModel() { EncodedProductId = productId });

        [HttpPost]
        public IActionResult NewAccessKey(EditAccessKeyViewModel key)
        {
            _treeValuesCache.AddAccessKey(key.ToModel((HttpContext.User as User).Id));

            return GetPartialProductAccessKeys(key.EncodedProductId);
        }

        [HttpPost]
        public IActionResult RemoveAccessKey([FromQuery(Name = "SelectedKey")] string keyId,
                                             [FromQuery(Name = "WithoutParents")] bool productsWithoutParent)
        {
            _treeValuesCache.RemoveAccessKey(Guid.Parse(keyId));

            return GetPartialAllAccessKeys(productsWithoutParent);
        }


        private List<AccessKeyViewModel> GetAvailableAccessKeys(bool onlyProductsWithoutParent)
        {
            var keys = new List<AccessKeyViewModel>(1 << 5);

            var availableProducts = _treeValuesCache.GetProducts(HttpContext.User as User, onlyProductsWithoutParent);
            foreach (var product in availableProducts)
            {
                if (_treeViewModel.Nodes.TryGetValue(product.Id, out var productViewModel))
                    keys.AddRange(productViewModel.AccessKeys.Values);
            }

            return keys;
        }

        private PartialViewResult GetPartialAllAccessKeys(bool onlyProductsWithoutParent) =>
            PartialView("_AllAccessKeys", GetAvailableAccessKeys(onlyProductsWithoutParent));

        private PartialViewResult GetPartialProductAccessKeys(string productId)
        {
            _treeViewModel.Nodes.TryGetValue(SensorPathHelper.Decode(productId), out var node);

            return PartialView("_ProductAccessKeys", node);
        }
    }
}
