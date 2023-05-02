using HSMServer.Constants;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;

namespace HSMServer.Filters
{
    /// <summary>
    /// The attribute denies access to some actions for a user who is neither admin or has role from _roles
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public abstract class UserRoleFilterBase : Attribute, IActionFilter
    {
        private readonly List<ProductRoleEnum> _roles;
        private readonly RedirectToActionResult _redirectToHomeIndex =
            new(ViewConstants.IndexAction, ViewConstants.HomeController, null);

        protected abstract string ArgumentName { get; }


        public UserRoleFilterBase(params ProductRoleEnum[] parameters)
        {
            _roles = new List<ProductRoleEnum>(parameters);
        }


        public void OnActionExecuting(ActionExecutingContext context)
        {
            var user = context.HttpContext.User as User;
            if (user?.IsAdmin ?? false) //Admins have all possible access
                return;

            if (TryGetEntityId(context, out var folderId))
                foreach (var role in _roles)
                    if (HasRole(user, folderId, role))
                        return;

            context.Result = _redirectToHomeIndex;
        }

        public void OnActionExecuted(ActionExecutedContext context) { }

        protected abstract bool HasRole(User user, Guid? entityId, ProductRoleEnum role);

        protected abstract Guid? GetEntityId(object arg, ActionExecutingContext context = null);

        protected virtual bool TryGetEntityId(ActionExecutingContext context, out Guid? folderId)
        {
            folderId = null;

            if (context.ActionArguments.TryGetValue(ArgumentName, out var arg))
                folderId = GetEntityId(arg, context);

            return folderId != null;
        }
    }
}
