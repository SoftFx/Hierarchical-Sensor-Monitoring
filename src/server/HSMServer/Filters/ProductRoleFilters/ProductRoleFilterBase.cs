using HSMServer.Constants;
using HSMServer.Controllers;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Filters.ProductRoleFilters
{
    /// <summary>
    /// The attribute denies access to some actions for a user who is neither admin or has role from _roles
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public abstract class ProductRoleFilterBase : Attribute, IActionFilter
    {
        private readonly List<ProductRoleEnum> _roles;
        private readonly RedirectToActionResult _redirectToHomeIndex =
            new(ViewConstants.IndexAction, ViewConstants.HomeController, null);

        protected abstract string ArgumentName { get; }


        public ProductRoleFilterBase(params ProductRoleEnum[] parameters)
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
                    if (user.ProductsRoles.Any(r => r.Item1 == productId && r.Item2 == role))
                        return;

            context.Result = _redirectToHomeIndex;
        }

        public void OnActionExecuted(ActionExecutedContext context) { }

        protected abstract Guid? GetProductId(object arg, ActionExecutingContext context = null);

        private bool TryGetProductId(ActionExecutingContext context, out Guid? productId)
        {
            productId = null;

            if (context.ActionArguments.TryGetValue(ArgumentName, out var arg))
                productId = GetProductId(arg, context);

            if (productId != null && context.Controller is AccessKeysController keysController)
            {
                var product = keysController.TreeValuesCache.GetProduct(productId.Value);
                while (product?.Parent != null)
                {
                    productId = product.Parent.Id;
                    product = product.Parent;
                }
            }

            return productId != null;
        }
    }
}
