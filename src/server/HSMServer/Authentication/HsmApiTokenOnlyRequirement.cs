using Microsoft.AspNetCore.Authorization;

namespace HSMServer.Authentication
{
    // Requires a principal shaped exactly as the HsmApiToken handler produces it: one
    // authenticated identity of the HsmApiToken scheme carrying the owner and token id
    // claims. A cookie-only principal, a mixed/multiple-identity principal, or an identity
    // that merely claims our scheme name without the handler's claims all fail closed.
    public sealed record HsmApiTokenOnlyRequirement : IAuthorizationRequirement;
}
