using System.Threading.Tasks;
using HSMCommon;
using HSMServer.Authentication;
using HSMServer.Configuration;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    public class TokenAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly UserManager _userManager;

        public TokenAuthMiddleware(RequestDelegate next, UserManager userManager)
        {
            _next = next;
            _userManager = userManager;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var port = context.Connection.LocalPort;

            if (port == Config.ApiPort)
            {
                context.User = _userManager.GetUserByCertificateThumbprint(CommonConstants.DefaultClientCertificateThumbprint);
            }

            await _next(context);
        }
    }
}
