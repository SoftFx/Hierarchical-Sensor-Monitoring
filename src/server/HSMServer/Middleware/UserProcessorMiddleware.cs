using HSMCommon.Constants;
using HSMServer.Authentication;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    public class UserProcessorMiddleware(RequestDelegate _next, IUserManager _userManager)
    {
        public Task InvokeAsync(HttpContext context)
        {
            var port = context.Connection.LocalPort;

            if (port == ConfigurationConstants.SitePort)
            {
                var currentUser = context.User;
                var correspondingUser = _userManager[currentUser?.Identity?.Name];
                context.User = correspondingUser;
            }

            return _next.Invoke(context);
        }
    }
}