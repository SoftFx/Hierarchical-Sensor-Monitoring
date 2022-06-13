using HSMServer.Constants;
using HSMServer.Core.Model.Authentication;
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
        private readonly string _productIdArgument = "productId";
        private readonly RedirectToActionResult _redirectToHomeIndex =
            new(ViewConstants.IndexAction, ViewConstants.HomeController, null);


        public ProductRoleFilter(params ProductRoleEnum[] parameters)
        {
            _roles = new List<ProductRoleEnum>(parameters);
        }


        public void OnActionExecuting(ActionExecutingContext context)
        {
            var user = context.HttpContext.User as User;
            if (user?.IsAdmin ?? false) //Admins have all possible access
                return;

            if (context.ActionArguments.TryGetValue(_productIdArgument, out var productIdArg) && productIdArg is string productId)
                foreach (var role in _roles)
                    if (user.ProductsRoles.Any(r => r.Key == productId && r.Value == role))
                        return;

            context.Result = _redirectToHomeIndex;
        }

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}
