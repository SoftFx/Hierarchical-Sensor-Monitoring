using HSMServer.Authentication;
using HSMServer.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;

namespace HSMServer.Filters
{
    /// <summary>
    /// The attribute denies access to EditProduct page for a user who is neither admin or the manager of the project
    /// </summary>
    public class ProductRoleFilter : Attribute, IActionFilter
    {
        private readonly List<ProductRoleEnum> _roles;
        private readonly string _parseArgument = "Product=";
        public ProductRoleFilter(params ProductRoleEnum[] parameters)
        {
            _roles = new List<ProductRoleEnum>();
            _roles.AddRange(parameters);
        }
        public void OnActionExecuting(ActionExecutingContext context)
        {
            var queryString = context.HttpContext.Request.QueryString.Value ?? string.Empty;
            var key = GetProductKey(queryString);
            var user = context.HttpContext.User as User;

            //Admins have all possible access
            if (user?.IsAdmin ?? false)
                return;
            
            foreach (var role in _roles)
            {
                if (user.ProductsRoles.FindIndex(r => r.Key == key && r.Value == role) != -1)
                    return;
            }

            context.Result = new RedirectToActionResult(ViewConstants.IndexAction, ViewConstants.HomeController, null);
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            //Empty body
        }

        private string GetProductKey(string address)
        {
            int index = address.IndexOf(_parseArgument);
            if (index != -1)
            {
                return address.Substring(index + _parseArgument.Length);
            }

            return address;
        }
    }
}
