using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Constants;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    public class UserProcessorMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IUserManager _userManager;

        public UserProcessorMiddleware(RequestDelegate next, IUserManager userManager)
        {
            _next = next;
            _userManager = userManager;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var port = context.Connection.LocalPort;
            if (port == ConfigurationConstants.ApiPort)
            {
                var currentUser = context.User;
                var correspondingUser = _userManager.GetUserByUserName(currentUser?.Identity?.Name);
                context.User = correspondingUser;
            }

            await _next.Invoke(context);
        }
    }
}
