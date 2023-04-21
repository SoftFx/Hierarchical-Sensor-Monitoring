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
using HSMServer.Constants;
using HSMServer.Model.Validators;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Mvc.Rendering;
using Org.BouncyCastle.Math.EC;

namespace HSMServer.Controllers
{
    [Authorize]
    public class AccessKeysController : BaseController
    {
        private readonly TreeViewModel _treeViewModel;

        internal ITreeValuesCache TreeValuesCache { get; }


        public AccessKeysController(ITreeValuesCache treeValuesCache, TreeViewModel treeViewModel)
        {
            TreeValuesCache = treeValuesCache;
            _treeViewModel = treeViewModel;
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
        public IActionResult AccessKeysForProduct([FromQuery(Name = "Selected")] string productId) => GetPartialProductAccessKeys(productId);

        
        [HttpGet]
        [AuthorizeIsAdmin(true)]
        public IActionResult NewServerAccessKey()
        {
            var productSelectItemsList = new List<SelectListItem>()
            {
                new ()
                {
                    Text = "All products",
                    Value = "1",
                    Selected = true
                }
            };
            
            return GetPartialNewAccessKey(new EditAccessKeyViewModel()
            {
                CloseModal = true,
                Products = TreeValuesCache.GetProducts().ToList()
            });
        }

        [HttpGet]
        [ProductRoleFilterByEncodedProductId(ProductRoleEnum.ProductManager)]
        public IActionResult NewAccessKey([FromQuery(Name = "Selected")] string encodedProductId,
                                          [FromQuery(Name = "CloseModal")] bool closeModal = false)
        {
            var decodedId= SensorPathHelper.DecodeGuid(encodedProductId);
           
            return GetPartialNewAccessKey(new EditAccessKeyViewModel()
            {
                EncodedProductId = encodedProductId,
                CloseModal = closeModal,
                SelectedProduct = TreeValuesCache.GetProductNameById(decodedId)
            });
        }

        [HttpPost]
        [ProductRoleFilterByKey(ProductRoleEnum.ProductManager)]
        public IActionResult NewAccessKey(EditAccessKeyViewModel key)
        {
            if (!ModelState.IsValid)
                return GetPartialNewAccessKey(key);

            TreeValuesCache.AddAccessKey(key.ToModel(CurrentUser.Id));
            if (string.IsNullOrEmpty(key.EncodedProductId))
                key.EncodedProductId = key.SelectedProduct;
            
            TreeValuesCache.AddAccessKey(key.ToModel(CurrentUser.Id));

            if (string.IsNullOrEmpty(key.EncodedProductId))
                return PartialView("_AllAccessKeys", GenerateFullViewModel());
            
            return GetPartialProductAccessKeys(key.EncodedProductId);
        }

        [HttpGet]
        [ProductRoleFilterBySelectedKey(ProductRoleEnum.ProductManager)]
        public IActionResult ModifyAccessKey([FromQuery] string selectedKey, [FromQuery] bool closeModal = false)
        {
            var key = TreeValuesCache.GetAccessKey(Guid.Parse(selectedKey));
            
            return GetPartialNewAccessKey(
                new EditAccessKeyViewModel(key)
                {
                    EncodedProductId = SensorPathHelper.EncodeGuid(key.ProductId),
                    CloseModal = closeModal,
                    IsModify = true,
                    SelectedProduct = TreeValuesCache.GetProductNameById(key.ProductId)
                });
        }

        [HttpPost]
        [ProductRoleFilterByKey(ProductRoleEnum.ProductManager)]
        public IActionResult ModifyAccessKey(EditAccessKeyViewModel key)
        {
            if (!ModelState.IsValid)
                return GetPartialNewAccessKey(key);

            TreeValuesCache.UpdateAccessKey(key.ToAccessKeyUpdate());

            return GetPartialProductAccessKeys(key.EncodedProductId);
        }

        [HttpPost]
        [ProductRoleFilterBySelectedKey(ProductRoleEnum.ProductManager)]
        public IActionResult RemoveAccessKeyFromAllTable([FromQuery(Name = "SelectedKey")] string selectedKey, [FromQuery] bool fullTable)
        {
            var key = TreeValuesCache.RemoveAccessKey(Guid.Parse(selectedKey));

            if (fullTable)
                return AvailableAccessKeys();

            return PartialView("_AllAccessKeys", GenerateShortViewModel(key.ProductId));
        }

        [HttpPost]
        [ProductRoleFilterBySelectedKey(ProductRoleEnum.ProductManager)]
        public IActionResult BlockAccessKeyFromAllTable([FromQuery] string selectedKey, [FromQuery] KeyState updatedState, [FromQuery] bool fullTable)
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

        private PartialViewResult GetPartialNewAccessKey(EditAccessKeyViewModel key) => PartialView("_NewAccessKey", key);

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
               serverKeys.AddRange(TreeValuesCache.GetAccessKeys().Where(x => x.ProductId == Guid.Empty).Select(x => new AccessKeyViewModel(x, null, CurrentUser.Name)).ToList());
            
            serverKeys.AddRange(keys);
            
            return serverKeys;
        }
    }
}