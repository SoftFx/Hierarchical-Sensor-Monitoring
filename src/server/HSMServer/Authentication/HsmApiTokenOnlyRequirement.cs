using Microsoft.AspNetCore.Authorization;

namespace HSMServer.Authentication
{
    // Requires a principal shaped exactly as the HsmApiToken handler produces it: one
    // authenticated identity of the HsmApiToken scheme carrying the owner and token id
    // claims. A cookie-only principal, a mixed/multiple-identity principal, or an identity
    // that merely claims our scheme name without the handler's claims all fail closed.
// The same requirement also backs the read-only flag's method-shaped backstop (#1384):
// unsafe HTTP methods never pass a read-only credential, independent of the evaluator.
    public sealed record HsmApiTokenOnlyRequirement : IAuthorizationRequirement;
}
