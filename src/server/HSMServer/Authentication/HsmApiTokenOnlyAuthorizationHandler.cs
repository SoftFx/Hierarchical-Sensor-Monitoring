using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace HSMServer.Authentication
{
    // The management policy always authenticates through the HsmApiToken scheme only
    // (the scheme is pinned inside the policy), so by the time this handler runs the
    // principal can only come from HsmApiTokenHandler. It still verifies the structural
    // invariant — exactly one identity, of our scheme, with the handler's claims — so a
    // principal assembled any other way never reaches a management action.
    public sealed class HsmApiTokenOnlyAuthorizationHandler
        : AuthorizationHandler<HsmApiTokenOnlyRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
            HsmApiTokenOnlyRequirement requirement)
        {
            // Multiple identities fail closed as a denial, never as an exception.
            using var identities = context.User.Identities.GetEnumerator();

            if (!identities.MoveNext())
                return Task.CompletedTask;

            var identity = identities.Current;

            if (identities.MoveNext())
                return Task.CompletedTask;

            if (identity is { IsAuthenticated: true } &&
                identity.AuthenticationType == HsmApiTokenDefaults.AuthenticationScheme &&
                identity.FindFirst(HsmApiTokenClaims.OwnerUserId) is not null &&
                identity.FindFirst(HsmApiTokenClaims.TokenId) is not null)
            {
                context.Succeed(requirement);
            }

            // Deliberately no Fail(): other requirements on the combined policy still get
            // their vote; silence here keeps the denial generic.

            return Task.CompletedTask;
        }
    }
}
