using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Filters.ProductRoleFilters;
using HSMServer.Helpers;
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
using ProductModel = HSMServer.Core.Model.ProductModel;

namespace HSMServer.Controllers
{
    [Authorize]
    public class AccessKeysController : BaseController
    {
        private readonly TreeViewModel _treeViewModel;
        private readonly IUserManager _userManager;

        internal ITreeValuesCache TreeValuesCache { get; }


        public AccessKeysController(ITreeValuesCache treeValuesCache, IUserManager userManager, TreeViewModel treeViewModel)
        {
            TreeValuesCache = treeValuesCache;
            _treeViewModel = treeViewModel;
            _userManager = userManager;
        }

        public IActionResult Index() => View(GenerateFullViewModel());

        [HttpGet]
        public IActionResult SearchKeyResult([FromQuery(Name = "SearchKey")] string searchKey)
        {
            searchKey ??= string.Empty;
            return PartialView("_AllAccessKeys", GenerateFullViewModel(searchKey));
        }

        [HttpGet]
        public IActionResult AvailableAccessKeys() => PartialView("_AllAccessKeys", GenerateFullViewModel());

        [HttpGet]
        public IActionResult AccessKeysForProduct([FromQuery(Name = "Selected")] string productId) => 
            GetPartialProductAccessKeys(productId);
        
        [HttpGet]
        [AuthorizeIsAdmin(true)]
        public IActionResult NewServerAccessKey()
        {
            return GetPartialNewAccessKey(new EditAccessKeyViewModel()
            {
                CloseModal = true,
                Products = TreeValuesCache.GetProducts().ToList(),
                ReturnType = AccessKeyReturnType.Table,
                SelectedProductId = Guid.Empty.ToString()
            });
        }

        [HttpGet]
        [ProductRoleFilterByEncodedProductId(ProductRoleEnum.ProductManager)]
        public IActionResult NewAccessKey([FromQuery(Name = "Selected")] string encodedProductId,
                                          [FromQuery] bool closeModal = false,
                                          [FromQuery] AccessKeyReturnType returnType = AccessKeyReturnType.Modal)
        {
            var key = new EditAccessKeyViewModel()
            {
                SelectedProductId = encodedProductId,
                CloseModal = closeModal,
                ReturnType = returnType,
                Products = new List<ProductModel>()
                {
                    TreeValuesCache.GetProduct(Guid.Parse(encodedProductId))
                }
            };

            return GetPartialNewAccessKey(key);
        }

        [HttpPost]
        [ProductRoleFilterByKey(ProductRoleEnum.ProductManager)]
        public IActionResult NewAccessKey(EditAccessKeyViewModel key)
        {
            if (!ModelState.IsValid)
            {
                if (key.ReturnType is not AccessKeyReturnType.Table)
                    return GetPartialNewAccessKey(key.ToNotModify(TreeValuesCache.GetProduct(Guid.Parse(key.SelectedProductId))));

                if (CurrentUser.IsAdmin)
                {
                    key.Products = TreeValuesCache.GetProducts().ToList();
                    key.IsModify = false;
                }

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
        [ProductRoleFilterBySelectedKey(ProductRoleEnum.ProductManager)]
        public IActionResult ModifyAccessKey([FromQuery] string selectedKey, [FromQuery] bool closeModal = false)
        {
            var key = TreeValuesCache.GetAccessKey(Guid.Parse(selectedKey));

            return GetPartialNewAccessKey(
                new EditAccessKeyViewModel(key)
                {
                    CloseModal = closeModal,
                    IsModify = true,
                    Products = new List<ProductModel>()
                    {
                        key.ProductId == Guid.Empty ? new ProductModel("All products") : TreeValuesCache.GetProduct(key.ProductId)
                    }
                });
        }

        [HttpPost]
        [ProductRoleFilterByKey(ProductRoleEnum.ProductManager)]
        public IActionResult ModifyAccessKey(EditAccessKeyViewModel key)
        {
            if (!ModelState.IsValid)
            {
                key.Products = new List<ProductModel>()
                {
                    key.SelectedProductId == Guid.Empty.ToString()
                        ? new ProductModel("All products")
                        : TreeValuesCache.GetProduct(Guid.Parse(key.SelectedProductId))
                };
                key.IsModify = true;

                return GetPartialNewAccessKey(key);
            }

            TreeValuesCache.UpdateAccessKey(key.ToAccessKeyUpdate());

            if (key.CloseModal)
                return Ok();

            return GetPartialProductAccessKeys(key.SelectedProductId);
        }

        [HttpPost]
        [ProductRoleFilterBySelectedKey(ProductRoleEnum.ProductManager)]
        public IActionResult RemoveAccessKeyFromAllTable([FromQuery(Name = "SelectedKey")] string selectedKey,
                                                         [FromQuery] bool fullTable)
        {
            var key = TreeValuesCache.RemoveAccessKey(Guid.Parse(selectedKey));

            if (fullTable)
                return AvailableAccessKeys();

            return PartialView("_AllAccessKeys", GenerateShortViewModel(key.ProductId));
        }

        [HttpPost]
        [ProductRoleFilterBySelectedKey(ProductRoleEnum.ProductManager)]
        public IActionResult BlockAccessKeyFromAllTable([FromQuery] string selectedKey,
                                                        [FromQuery] KeyState updatedState, [FromQuery] bool fullTable)
        {
            var key = TreeValuesCache.GetAccessKey(Guid.Parse(selectedKey));
            if (updatedState == KeyState.Active && key.IsExpired)
                updatedState = KeyState.Expired;

            TreeValuesCache.UpdateAccessKeyState(Guid.Parse(selectedKey), updatedState);

            if (fullTable)
                return AvailableAccessKeys();

            return PartialView("_AllAccessKeys", GenerateShortViewModel(key.ProductId));
        }

        private PartialViewResult GetPartialProductAccessKeys(string productId)
        {
            _treeViewModel.Nodes.TryGetValue(SensorPathHelper.DecodeGuid(productId), out var node);

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
                serverKeys.AddRange(TreeValuesCache.GetAccessKeys().Where(x => x.ProductId == Guid.Empty)
                                                                           .Select(x => new AccessKeyViewModel(x, null, x.AuthorId.HasValue ? (_userManager[x.AuthorId.Value]?.Name ?? x.AuthorId.ToString()) : x.AuthorId?.ToString())).ToList());

            serverKeys.AddRange(keys);

            return serverKeys;
        }
    }
}