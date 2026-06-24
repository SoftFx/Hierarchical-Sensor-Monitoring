using HSMServer.Constants;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.SlackRoleFilters
{
    /// <summary>
    /// Slack destinations are global (not folder-scoped), so any mutation
    /// requires administrator rights. Non-admins are redirected to Home/Index.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SlackAdminAttribute : Attribute, IAuthorizationFilter
    {
        private readonly RedirectToActionResult _redirectToHomeIndex = new(ViewConstants.IndexAction, ViewConstants.HomeController, null);

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User as User;

            if (user?.IsAdmin ?? false)
                return;

            context.Result = _redirectToHomeIndex;
        }
    }
}
