using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Authentication
{
    // The management policy always authenticates through the HsmApiToken scheme only
    // (the scheme is pinned inside the policy), so by the time this handler runs the
    // principal can only come from HsmApiTokenHandler. It still verifies the structural
    // invariant — exactly one identity, of our scheme, with the handler's claims — so a
    // principal assembled any other way never reaches a management action.
    //
    // It is also the belt-and-braces half of the read-only flag (#1384): the flag's
    // primary enforcement is the evaluator's per-resource AuthorizeWrite, but that
    // depends on every mutating action calling it. This handler adds a method-shaped
    // backstop on the policy itself — an unsafe HTTP method (POST/PUT/PATCH/DELETE)
    // never passes a read-only credential, whatever the action does. Method-shaped on
    // purpose: the denial carries no per-target information, so it cannot disagree with
    // the evaluator's 403/404 anti-enumeration split.
    public sealed class HsmApiTokenOnlyAuthorizationHandler
        : AuthorizationHandler<HsmApiTokenOnlyRequirement>
    {
        private static readonly string[] UnsafeMethods = ["POST", "PUT", "PATCH", "DELETE"];

        private readonly IApiTokenManager _tokens;

        public HsmApiTokenOnlyAuthorizationHandler(IApiTokenManager tokens)
        {
            _tokens = tokens;
        }

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

            if (identity is not { IsAuthenticated: true } ||
                identity.AuthenticationType != HsmApiTokenDefaults.AuthenticationScheme ||
                identity.FindFirst(HsmApiTokenClaims.OwnerUserId) is null ||
                identity.FindFirst(HsmApiTokenClaims.TokenId) is null)
            {
                return Task.CompletedTask;
            }

            if (IsUnsafeReadOnlyRequest(context, identity))
                return Task.CompletedTask;

            context.Succeed(requirement);

            // Deliberately no Fail(): other requirements on the combined policy still get
            // their vote; silence here keeps the denial generic.

            return Task.CompletedTask;
        }

        // True when an unsafe method carries a read-only credential. A token record that
        // cannot be resolved here (revoked/removed between authentication and this
        // check) does not block: the evaluator re-checks liveness per resource, and this
        // backstop exists for the actions that forget the evaluator, not against the
        // ones that remember it.
        private bool IsUnsafeReadOnlyRequest(AuthorizationHandlerContext context, ClaimsIdentity identity)
        {
            if (context.Resource is not HttpContext httpContext)
                return false;

            var method = httpContext.Request?.Method;

            if (method is null || !UnsafeMethods.Contains(method, StringComparer.OrdinalIgnoreCase))
                return false;

            var tokenId = identity.FindFirst(HsmApiTokenClaims.TokenId).Value;

            return _tokens.GetToken(tokenId)?.ReadOnly == true;
        }
    }
}
