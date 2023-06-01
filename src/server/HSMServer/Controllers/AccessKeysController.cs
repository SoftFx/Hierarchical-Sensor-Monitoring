﻿using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Filters.ProductRoleFilters;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Attributes;
using HSMServer.Authentication;

namespace HSMServer.Controllers
{
    [Authorize]
    public class AccessKeysController : BaseController
    {
        private readonly TreeViewModel _treeViewModel;

        internal ITreeValuesCache TreeValuesCache { get; }


        public AccessKeysController(ITreeValuesCache treeValuesCache, IUserManager userManager, TreeViewModel treeViewModel) : base(userManager)
        {
            TreeValuesCache = treeValuesCache;
            _treeViewModel = treeViewModel;
        }

        public IActionResult Index() => View(GenerateFullViewModel());

        [HttpGet]
        public IActionResult SearchKeyResult(string searchKey)
        {
            searchKey ??= string.Empty;
            return PartialView("_AllAccessKeys", GenerateFullViewModel(searchKey));
        }

        [HttpGet]
        public IActionResult AvailableAccessKeys() => PartialView("_AllAccessKeys", GenerateFullViewModel());

        [HttpGet]
        public IActionResult AccessKeysForProduct(string productId) => 
            GetPartialProductAccessKeys(Guid.Parse(productId));
        
        [HttpGet]
        [AuthorizeIsAdmin]
        public IActionResult NewServerAccessKey()
        {
            return GetPartialNewAccessKey(new EditAccessKeyViewModel()
            {
                CloseModal = true,
                Products = TreeValuesCache.GetProducts().ToList(),
                ReturnType = AccessKeyReturnType.Table,
                SelectedProductId = Guid.Empty
            });
        }

        [HttpGet]
        [ProductRoleFilterByEncodedProductId(nameof(selectedId), ProductRoleEnum.ProductManager)]
        public IActionResult NewAccessKey(string selectedId, bool closeModal = false, AccessKeyReturnType returnType = AccessKeyReturnType.Modal)
        {
            var key = new EditAccessKeyViewModel()
            {
                SelectedProductId = Guid.Parse(selectedId),
                CloseModal = closeModal,
                ReturnType = returnType,
                Products = new List<ProductModel>()
                {
                    TreeValuesCache.GetProduct(Guid.Parse(selectedId))
                }
            };

            return GetPartialNewAccessKey(key);
        }

        [HttpPost]
        [ProductRoleFilterByKey(nameof(key), ProductRoleEnum.ProductManager)]
        public IActionResult NewAccessKey(EditAccessKeyViewModel key)
        {
            if (!ModelState.IsValid)
            {
                if (key.ReturnType is not AccessKeyReturnType.Table)
                    return GetPartialNewAccessKey(key.ToNotModify(TreeValuesCache.GetProduct(key.SelectedProductId)));

                return GetPartialNewAccessKey(key.ToNotModify(CurrentUser.IsAdmin ? TreeValuesCache.GetProducts().ToArray() : Array.Empty<ProductModel>()));
            }

            TreeValuesCache.AddAccessKey(key.ToModel(CurrentUser.Id));

            if (key.CloseModal)
                return Ok();
            
            if (key.ReturnType is AccessKeyReturnType.Modal)
                return GetPartialProductAccessKeys(key.SelectedProductId);

            return PartialView("_AllAccessKeys", GenerateFullViewModel());
        }

        [HttpGet]
        [ProductRoleFilterBySelectedKey(nameof(selectedKey), ProductRoleEnum.ProductManager)]
        public IActionResult ModifyAccessKey(string selectedKey, bool closeModal = false)
        {
            var key = TreeValuesCache.GetAccessKey(Guid.Parse(selectedKey));

            return GetPartialNewAccessKey(new EditAccessKeyViewModel(key).ToModify(key.IsMaster ? null : TreeValuesCache.GetProduct(key.ProductId), closeModal));
        }

        [HttpPost]
        [ProductRoleFilterByKey(nameof(key), ProductRoleEnum.ProductManager)]
        public IActionResult ModifyAccessKey(EditAccessKeyViewModel key)
        {
            if (!ModelState.IsValid)
                return GetPartialNewAccessKey(key.ToModify(key.IsMaster ? null : TreeValuesCache.GetProduct(key.SelectedProductId), key.CloseModal));

            TreeValuesCache.UpdateAccessKey(key.ToAccessKeyUpdate());

            if (key.CloseModal)
                return Ok();

            return GetPartialProductAccessKeys(key.SelectedProductId);
        }

        [HttpPost]
        [ProductRoleFilterBySelectedKey(nameof(selectedKey), ProductRoleEnum.ProductManager)]
        public IActionResult RemoveAccessKeyFromAllTable(string selectedKey, bool fullTable)
        {
            var key = TreeValuesCache.RemoveAccessKey(Guid.Parse(selectedKey));

            if (fullTable)
                return AvailableAccessKeys();

            return PartialView("_AllAccessKeys", GenerateShortViewModel(key.ProductId));
        }

        [HttpPost]
        [ProductRoleFilterBySelectedKey(nameof(selectedKey), ProductRoleEnum.ProductManager)]
        public IActionResult BlockAccessKeyFromAllTable(string selectedKey, KeyState updatedState, bool fullTable)
        {
            var key = TreeValuesCache.GetAccessKey(Guid.Parse(selectedKey));
            if (updatedState == KeyState.Active && key.IsExpired)
                updatedState = KeyState.Expired;

            TreeValuesCache.UpdateAccessKeyState(Guid.Parse(selectedKey), updatedState);

            if (fullTable)
                return AvailableAccessKeys();

            return PartialView("_AllAccessKeys", GenerateShortViewModel(key.ProductId));
        }

        private PartialViewResult GetPartialProductAccessKeys(Guid productId)
        {
            _treeViewModel.Nodes.TryGetValue(productId, out var node);

            return PartialView("_ProductAccessKeys", node);
        }

        private PartialViewResult GetPartialNewAccessKey(EditAccessKeyViewModel key) =>
            PartialView("_NewAccessKey", key);

        private AccessKeyTableViewModel GenerateShortViewModel(Guid productId)
        {
            _treeViewModel.Nodes.TryGetValue(productId, out var productNode);
            return new()
            {
                Keys = productNode.GetAccessKeys()
            };
        }

        private AccessKeyTableViewModel GenerateFullViewModel(string searchKey = "")
        {
            return new()
            {
                Keys = GetAvailableAccessKeys().Where(x => x.Id.ToString().Contains(searchKey)).ToList(),
                FullTable = true
            };
        }

        private List<AccessKeyViewModel> GetAvailableAccessKeys()
        {
            var keys = new List<AccessKeyViewModel>(1 << 5);

            var availableProducts = _treeViewModel.GetUserProducts(CurrentUser);
            foreach (var product in availableProducts)
            {
                if (_treeViewModel.Nodes.TryGetValue(product.Id, out var productViewModel))
                    keys.AddRange(productViewModel.GetAccessKeys());
            }

            keys = keys.OrderBy(key => key?.NodePath).ToList();
            var serverKeys = new List<AccessKeyViewModel>(1 << 4);

            if (CurrentUser.IsAdmin)
                serverKeys.AddRange(TreeValuesCache.GetMasterKeys().Select(x => new AccessKeyViewModel(x, null, x.AuthorId)));

            serverKeys.AddRange(keys);

            return serverKeys;
        }
    }
}