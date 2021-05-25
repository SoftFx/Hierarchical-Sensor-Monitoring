using System.Threading.Tasks;
using HSMServer.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HSMServer.Middleware
{
    public class CustomCookieAuthenticationEvents : CookieAuthenticationEvents
    {
        private readonly IUserManager _userManager;
        public CustomCookieAuthenticationEvents(IUserManager userManager)
        {
            _userManager = userManager;
        }

        public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
        {
            var currentUser = context.HttpContext.User;

            var requiredUser = _userManager.GetUserByUserName(currentUser.Identity?.Name);
            context.HttpContext.User = requiredUser;
        }
    }
}
