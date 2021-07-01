using HSMServer.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;

namespace HSMServer.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class AuthorizeRoleAttribute : AuthorizeAttribute, IAuthorizationFilter
    {
        private List<UserRoleEnum> _policyRoles;

        public AuthorizeRoleAttribute(params UserRoleEnum[] roles)
        {
            _policyRoles = new List<UserRoleEnum>();
            _policyRoles.AddRange(roles);
        }
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var user = context.HttpContext.User;
            var convertedUser = user as User;

            if (_policyRoles.Contains(convertedUser.Role))
            {
                return;
            }

            context.Result = new UnauthorizedResult();
        }
    }
}
