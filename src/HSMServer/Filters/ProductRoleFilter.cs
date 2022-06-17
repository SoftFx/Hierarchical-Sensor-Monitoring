using HSMServer.Constants;
using HSMServer.Controllers;
using HSMServer.Core.Model.Authentication;
using HSMServer.Helpers;
using HSMServer.Model.AccessKeysViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Filters
{
    /// <summary>
    /// The attribute denies access to some actions for a user who is neither admin or has role from _roles
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ProductRoleFilter : Attribute, IActionFilter
    {
        private readonly List<ProductRoleEnum> _roles;
        private readonly RedirectToActionResult _redirectToHomeIndex =
            new(ViewConstants.IndexAction, ViewConstants.HomeController, null);

        private readonly string _encodedProductIdArgument = "encodedProductId";
        private readonly string _productIdArgument = "productId";
        private readonly string _selectedKeyArgument = "selectedKey";
        private readonly string _keyArgument = "key";


        public ProductRoleFilter(params ProductRoleEnum[] parameters)
        {
            _roles = new List<ProductRoleEnum>(parameters);
        }


        public void OnActionExecuting(ActionExecutingContext context)
        {
            var user = context.HttpContext.User as User;
            if (user?.IsAdmin ?? false) //Admins have all possible access
                return;

            if (TryGetProductId(context, out var productId))
                foreach (var role in _roles)
                    if (user.ProductsRoles.Any(r => r.Key == productId && r.Value == role))
                        return;

            context.Result = _redirectToHomeIndex;
        }

        public void OnActionExecuted(ActionExecutedContext context) { }

        private bool TryGetProductId(ActionExecutingContext context, out string productId)
        {
            if (context.ActionArguments.TryGetValue(_productIdArgument, out var productIdArg) && productIdArg is string)
                productId = productIdArg as string;
            else if (context.ActionArguments.TryGetValue(_encodedProductIdArgument, out productIdArg) && productIdArg is string encodedProductId)
                productId = SensorPathHelper.Decode(encodedProductId);
            else if (context.ActionArguments.TryGetValue(_keyArgument, out var keyArg) && keyArg is EditAccessKeyViewModel key)
                productId = SensorPathHelper.Decode(key.EncodedProductId);
            else if (context.ActionArguments.TryGetValue(_selectedKeyArgument, out var selectedKeyArg) && selectedKeyArg is string selectedKey)
            {
                var accessKey = (context.Controller as AccessKeysController).TreeValuesCache.GetAccessKey(Guid.Parse(selectedKey));
                productId = accessKey.ProductId;
            }
            else
            {
                productId = null;
                return false;
            }

            return true;
        }
    }
}
