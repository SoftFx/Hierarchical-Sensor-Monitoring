using System.Threading.Tasks;
using HSMCommon;
using HSMServer.Authentication;
using HSMServer.Configuration;
using HSMServer.Constants;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    internal class TokenAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IUserManager _userManager;

        public TokenAuthMiddleware(RequestDelegate next, IUserManager userManager)
        {
            _next = next;
            _userManager = userManager;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var port = context.Connection.LocalPort;

            if (port == ConfigurationConstants.ApiPort)
            {
                context.User = _userManager.GetUserByCertificateThumbprint(CommonConstants.DefaultClientCertificateThumbprint);
            }

            await _next(context);
        }
    }
}
