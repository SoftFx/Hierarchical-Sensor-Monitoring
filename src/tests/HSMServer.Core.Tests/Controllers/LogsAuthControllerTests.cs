using System.Security.Claims;
using HSMServer.Controllers;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // Caddy's forward_auth gate for the VictoriaLogs read routes (#1580): the
    // endpoint's status code IS the verdict — 204 admits an admin, a browser
    // navigation without a session gets the login redirect (Caddy passes the
    // non-2xx answer back to the client as-is), every other anonymous client
    // gets a plain 401, and a logged-in non-admin gets 403. Authentication is
    // recognized by the UserProcessorMiddleware principal shape (a User model),
    // not by ClaimsPrincipal.Identity: the stored User carries no identity, and
    // a bearer/HsmApiToken principal never becomes one.
    public class LogsAuthControllerTests
    {
        [Fact]
        public void Check_AdminSession_Is204()
        {
            var result = Build(new User("admin") { IsAdmin = true }, "application/json").Check();

            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public void Check_NonAdminUser_Is403()
        {
            var result = Build(new User("viewer"), "text/html,application/xhtml+xml,*/*;q=0.8").Check();

            var status = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
        }

        [Theory]
        [InlineData("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")]
        [InlineData("application/xhtml+xml, text/html;q=0.9")]
        public void Check_AnonymousBrowser_IsRedirectedToLogin(string accept)
        {
            var result = Build(new ClaimsPrincipal(new ClaimsIdentity()), accept).Check();

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("/Account/Index", redirect.Url);
        }

        [Theory]
        [InlineData("application/json")]
        [InlineData("*/*")]
        [InlineData("")]
        public void Check_AnonymousApiClient_IsPlain401(string accept)
        {
            var result = Build(new ClaimsPrincipal(new ClaimsIdentity()), accept).Check();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public void Check_DeletedUserPrincipal_IsRedirectedToLogin()
        {
            // UserProcessorMiddleware sets a null principal when the stored user
            // disappeared; the gate must fail closed, not throw.
            var result = Build(null, "text/html").Check();

            Assert.IsType<RedirectResult>(result);
        }

        [Fact]
        public void Check_AuthenticatedNonCookiePrincipal_Is401()
        {
            // An HsmApiToken bearer principal passes through UserProcessorMiddleware
            // untouched: it is authenticated but is not a User, so it must not see
            // the logs — only an HSM admin session does.
            var principal = new ClaimsPrincipal(new ClaimsIdentity("Bearer"));

            var result = Build(principal, "application/json").Check();

            Assert.IsType<UnauthorizedResult>(result);
        }

        private static LogsAuthController Build(ClaimsPrincipal user, string accept)
        {
            var http = new DefaultHttpContext();
            if (user is not null)
                http.User = user;
            http.Request.Headers.Accept = accept;

            return new LogsAuthController
            {
                ControllerContext = new ControllerContext { HttpContext = http },
            };
        }
    }
}
