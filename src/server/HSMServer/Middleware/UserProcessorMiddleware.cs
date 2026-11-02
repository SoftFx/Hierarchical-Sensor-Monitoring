using HSMServer.Authentication;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    // Resolves the cookie principal into the stored user on every request. Until #1590 this
    // ran on the site listener only; with the single listener there is no port dimension,
    // and the token-principal pass-through below is what keeps API-token requests intact.
    public sealed class UserProcessorMiddleware(RequestDelegate _next, IUserManager _userManager)
    {
        public Task InvokeAsync(HttpContext context)
        {
            // An API-token principal is exactly what the HsmApiToken handler produced:
            // identities with owner/token claims and no login name. It must pass
            // through untouched — replacing it with a stored user here would restore
            // unrestricted owner rights behind the token's grants. ANY identity of the
            // scheme counts, not just the primary one: the check must not depend on how
            // another component ordered the merged identities.
            if (context.User?.Identities.Any(identity =>
                    identity.AuthenticationType == HsmApiTokenDefaults.AuthenticationScheme) == true)
                return _next.Invoke(context);

            var currentUser = context.User;
            var correspondingUser = _userManager[currentUser?.Identity?.Name];
            context.User = correspondingUser;

            return _next.Invoke(context);
        }
    }
}
