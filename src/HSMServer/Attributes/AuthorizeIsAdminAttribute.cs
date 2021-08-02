using HSMServer.Authentication;
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
        private List<bool> _policyRoles;

        public AuthorizeIsAdminAttribute(params bool[] roles)
        {
            _policyRoles = new List<bool>();
            _policyRoles.AddRange(roles);
        }
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var user = context.HttpContext.User;

            if (user is User convertedUser && _policyRoles.Contains(convertedUser.IsAdmin))
            {
                return;
            }

            context.Result = new UnauthorizedResult();
        }
    }
}
