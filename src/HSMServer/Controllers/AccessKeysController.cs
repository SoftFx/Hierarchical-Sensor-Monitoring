﻿using HSMServer.Core.Cache;
using HSMServer.Core.Model.Authentication;
using HSMServer.Filters;
using HSMServer.Helpers;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.TreeViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;

namespace HSMServer.Controllers
{
    [Authorize]
    public class AccessKeysController : Controller
    {
        private readonly ITreeValuesCache _treeValuesCache;
        private readonly TreeViewModel _treeViewModel;


        public AccessKeysController(ITreeValuesCache treeValuesCache, TreeViewModel treeViewModel)
        {
            _treeValuesCache = treeValuesCache;
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
        public IActionResult NewAccessKey([FromQuery(Name = "Selected")] string productId,
                                          [FromQuery(Name = "CloseModal")] bool closeModal = false) =>
            GetPartialNewAccessKey(
                new EditAccessKeyViewModel()
                {
                    EncodedProductId = productId,
                    CloseModal = closeModal,
                });

        [HttpPost]
        public IActionResult NewAccessKey(EditAccessKeyViewModel key)
        {
            if (!ModelState.IsValid)
                return GetPartialNewAccessKey(key);

            _treeValuesCache.AddAccessKey(key.ToModel((HttpContext.User as User).Id));

            return GetPartialProductAccessKeys(key.EncodedProductId);
        }

        [HttpGet]
        public IActionResult ModifyAccessKey([FromQuery(Name = "SelectedKey")] string selectedKey)
        {
            var key = _treeValuesCache.GetAccessKey(Guid.Parse(selectedKey));

            return GetPartialNewAccessKey(
                new EditAccessKeyViewModel(key)
                {
                    EncodedProductId = SensorPathHelper.Encode(key.ProductId),
                    CloseModal = true,
                    IsModify = true,
                });
        }

        [HttpPost]
        public IActionResult ModifyAccessKey(EditAccessKeyViewModel key)
        {
            if (!ModelState.IsValid)
                return GetPartialNewAccessKey(key);

            _treeValuesCache.UpdateAccessKey(key.ToAccessKeyUpdate());

            return GetPartialNewAccessKey(key);
        }

        [HttpPost]
        public IActionResult RemoveAccessKeyFromAllTable([FromQuery(Name = "SelectedKey")] string keyId,
                                                         [FromQuery(Name = "AllProducts")] bool isAllProducts)
        {
            _treeValuesCache.RemoveAccessKey(Guid.Parse(keyId));

            return GetPartialAllAccessKeys(isAllProducts);
        }

        [HttpPost]
        public IActionResult RemoveAccessKeyFromProductTable([FromQuery(Name = "SelectedKey")] string selectedKey)
        {
            var accessKeyId = Guid.Parse(selectedKey);

            _treeViewModel.AccessKeys.TryGetValue(accessKeyId, out var key);
            _treeViewModel.Nodes.TryGetValue(key.ParentProduct.Id, out var productNode);

            _treeValuesCache.RemoveAccessKey(accessKeyId);

            return PartialView("_AllAccessKeys", productNode.GetAccessKeys());
        }


        private PartialViewResult GetPartialAllAccessKeys(bool isAllProducts) =>
            PartialView("_AllAccessKeys", GetAvailableAccessKeys(isAllProducts));

        private List<AccessKeyViewModel> GetAvailableAccessKeys(bool isAllProducts)
        {
            var user = HttpContext.User as User;
            var keys = new List<AccessKeyViewModel>(1 << 5);

            _treeViewModel.UpdateAccessKeysCharacteristics(user);

            var availableProducts = _treeValuesCache.GetProducts(user, isAllProducts);
            foreach (var product in availableProducts)
            {
                if (_treeViewModel.Nodes.TryGetValue(product.Id, out var productViewModel))
                    keys.AddRange(productViewModel.GetAccessKeys());
            }

            keys.Sort((key1, key2) => key1.NodePath.CompareTo(key2.NodePath));

            return keys;
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
