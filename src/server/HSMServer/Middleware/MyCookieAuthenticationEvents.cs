using System.Threading.Tasks;
using HSMServer.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HSMServer.Middleware;

public class MyCookieAuthenticationEvents : CookieAuthenticationEvents
{
    private readonly IUserManager _userManager;

    public MyCookieAuthenticationEvents(IUserManager userManager)
    {
        _userManager = userManager;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (_userManager[context.Principal?.Identity?.Name] == null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
