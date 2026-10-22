using System;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    // Caddy's forward_auth gate for the VictoriaLogs read routes (#1580): the
    // request arrives rewritten to GET /logs-auth carrying the client's session
    // cookie, and the verdict travels as the status code alone — Caddy admits on
    // 204 and passes any other answer (login redirect, 401, 403) back to the
    // client unchanged. Anonymous here means "no UserProcessorMiddleware user
    // model": the stored User has no ClaimsIdentity, so principal shape, not
    // Identity.IsAuthenticated, distinguishes a cookie session — and a bearer or
    // HsmApiToken principal stays a plain ClaimsPrincipal that never passes.
    // Excluded from the OpenAPI document like /mcp: an infrastructure probe, not
    // a REST agent's surface.
    [Route("logs-auth")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class LogsAuthController : Controller
    {
        [HttpGet]
        public IActionResult Check()
        {
            if (HttpContext.User is Model.Authentication.User { IsAdmin: true })
                return NoContent();

            // A human following a link lands on the familiar login page; vmui's
            // fetches and curl get a plain 401 instead of login HTML.
            if (HttpContext.User is not Model.Authentication.User)
                return Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase)
                    ? Redirect("/Account/Index")
                    : Unauthorized();

            return new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}
