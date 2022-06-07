using HSMServer.Core.Cache;
using HSMServer.Core.Model.Authentication;
using HSMServer.Helpers;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.TreeViewModels;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;

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


        public IActionResult Index() => View(_treeViewModel.AccessKeys.Values.ToList());

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
        public void RemoveAccessKey([FromQuery(Name = "SelectedKey")] string keyId) =>
            _treeValuesCache.RemoveAccessKey(Guid.Parse(keyId));

        private PartialViewResult GetPartialProductAccessKeys(string productId)
        {
            _treeViewModel.Nodes.TryGetValue(SensorPathHelper.Decode(productId), out var node);

            return PartialView("_ProductAccessKeys", node);
        }
    }
}
