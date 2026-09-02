using System.Threading.Tasks;
using HSMServer.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

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

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        // The cookie-authorized /api/v1 family (the reserved token-management routes)
        // answers a failed authorization with a plain 401, never the HTML login
        // redirect — the management area's contract holds for every endpoint in it.
        // Every other path keeps the browser login redirect.
        if (context.HttpContext.Request.Path.StartsWithSegments(HsmApiTokenDefaults.ManagementAreaPath))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        return base.RedirectToLogin(context);
    }
}
