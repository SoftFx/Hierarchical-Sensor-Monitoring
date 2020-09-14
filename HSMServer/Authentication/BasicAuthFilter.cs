using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using HSMServer.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.Extensions.DependencyInjection;

namespace HSMServer.Authentication
{
    public class BasicAuthFilter : IAuthorizationFilter
    {
        //private readonly string _realm;
        private readonly List<User> _users;
        public BasicAuthFilter()
        {
            _users = Config.Users;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            try
            {
                string authHeader = context.HttpContext.Request.Headers["Authorization"];
                if (authHeader != null)
                {
                    var authHeaderValue = AuthenticationHeaderValue.Parse(authHeader);
                    if (authHeaderValue.Scheme.Equals(AuthenticationSchemes.Basic.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        var credentials = Encoding.ASCII
                            .GetString(Convert.FromBase64String(authHeaderValue.Parameter ?? string.Empty))
                            .Split(':', 2);
                        if (credentials.Length == 2)
                        {
                            if (IsAuthorized(context, credentials[0], credentials[1]))
                            {
                                return;
                            }
                        }
                    }
                }

                ReturnUnauthorizedResult(context);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public bool IsAuthorized(AuthorizationFilterContext context, string login, string password)
        {
            var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
            return userService.IsAuthorized(login, password);
        }

        private void ReturnUnauthorizedResult(AuthorizationFilterContext context)
        {
            context.HttpContext.Response.Headers["WWW-Authenticate"] = "Basic auth failed";
            context.Result = new UnauthorizedResult();
        }
    }
}
