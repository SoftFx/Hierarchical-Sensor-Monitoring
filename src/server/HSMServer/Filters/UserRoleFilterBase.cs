using HSMServer.Constants;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Http;
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
        private readonly RedirectToActionResult _redirectToHomeIndex = new(ViewConstants.IndexAction, ViewConstants.HomeController, null);

        private readonly string _argumentName;

        protected readonly List<ProductRoleEnum> _roles;


        public UserRoleFilterBase(string argumentName, params ProductRoleEnum[] parameters)
        {
            _roles = new List<ProductRoleEnum>(parameters);
            _argumentName = argumentName;
        }


        public void OnActionExecuting(ActionExecutingContext context)
        {
            var user = context.HttpContext.User as User;
            if ((user?.IsAdmin ?? false) || TryCheckRole(context, user)) //Admins have all possible access
                return;

            // AJAX callers (e.g. EditChat's removeChannel POST) follow 302→/Home/Index and end up
            // with a 200 HTML response — jQuery's `success` fires and the caller reloads the form
            // looking like the write succeeded when the action was never authorised to run. Return
            // 403 so jQuery's `error` runs and the user sees the failure toast. Non-AJAX callers
            // (typed URLs, form GETs) keep the legacy redirect-to-home behavior.
            context.Result = IsAjaxRequest(context)
                ? new StatusCodeResult(StatusCodes.Status403Forbidden)
                : _redirectToHomeIndex;
        }

        public void OnActionExecuted(ActionExecutedContext context) { }


        private static bool IsAjaxRequest(ActionExecutingContext context) =>
            context.HttpContext.Request.Headers.TryGetValue("X-Requested-With", out var value)
            && string.Equals(value, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);


        protected abstract bool HasRole(User user, Guid? entityId, ProductRoleEnum role);

        protected abstract Guid? GetEntityId(object arg, ActionExecutingContext context = null);


        protected virtual bool TryCheckRole(ActionExecutingContext context, User user)
        {
            if (TryGetEntityId(context, out var entityId))
                foreach (var role in _roles)
                    if (HasRole(user, entityId, role))
                        return true;

            return false;
        }

        protected virtual bool TryGetEntityId(ActionExecutingContext context, out Guid? entityId)
        {
            entityId = null;

            if (context.ActionArguments.TryGetValue(_argumentName, out var arg))
                entityId = GetEntityId(arg, context);

            return entityId != null;
        }
    }
}
