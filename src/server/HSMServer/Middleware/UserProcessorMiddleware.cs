using HSMServer.Authentication;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using System.Linq;
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
            }

            return _next.Invoke(context);
        }
    }
}