using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;

namespace HSMServer.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class AuthorizeIsAdminAttribute : AuthorizeAttribute, IAuthorizationFilter
    { 
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (context is null)
                throw new ArgumentNullException(nameof(context));
            
            if (context.HttpContext.User is User { IsAdmin: true })
                return;
            
            context.Result = new UnauthorizedResult();
        }
    }
}
