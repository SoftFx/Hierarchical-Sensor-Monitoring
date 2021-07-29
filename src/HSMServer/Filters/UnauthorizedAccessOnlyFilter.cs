using HSMServer.Authentication;
using HSMServer.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters
{
    public class UnauthorizedAccessOnlyFilter : Attribute, IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            var user = context.HttpContext.User as User;
            if (user != null)
            {
                context.Result = new RedirectToActionResult(ViewConstants.IndexAction, ViewConstants.HomeController, null);
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            //throw new NotImplementedException();
        }
    }
}
