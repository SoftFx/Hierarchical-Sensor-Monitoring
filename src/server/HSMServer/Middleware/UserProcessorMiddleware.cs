using HSMServer.Authentication;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    public class UserProcessorMiddleware(RequestDelegate _next, IUserManager _userManager, IServerConfig config)
    {
        private readonly int _sitePort = config.Kestrel.SitePort;


        public Task InvokeAsync(HttpContext context)
        {
            var port = context.Connection.LocalPort;

            if (port == _sitePort)
            {
                var currentUser = context.User;
                var correspondingUser = _userManager[currentUser?.Identity?.Name];
                context.User = correspondingUser;
            }

            return _next.Invoke(context);
        }
    }
}