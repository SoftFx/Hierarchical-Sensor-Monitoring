using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Middleware;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // The reserved cookie-only /api/v1 family authorizes through the cookie DefaultPolicy,
    // so a failed authorization challenges the cookie scheme — whose default answer is the
    // LoginPath redirect. The events override keeps the management area's contract there:
    // a plain non-redirecting 401, while every legacy path keeps its browser redirect.
    public class MyCookieAuthenticationEventsApiTokenTests
    {
        [Fact]
        public async Task RedirectToLogin_InManagementArea_IsPlain401()
        {
            var (events, context) = Build("/api/v1/api-tokens");

            await events.RedirectToLogin(BuildRedirectContext(context));

            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
            Assert.True(StringValues.IsNullOrEmpty(context.Response.Headers.Location));
        }

        [Theory]
        [InlineData("/Home/Index")]
        [InlineData("/Account/Index")]
        [InlineData("/api/sensors")]        // outside the management area, unlike /api/v1
        public async Task RedirectToLogin_OutsideManagementArea_KeepsTheLoginRedirect(string path)
        {
            var (events, context) = Build(path);

            await events.RedirectToLogin(BuildRedirectContext(context));

            Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
            Assert.StartsWith("/Account/Index", context.Response.Headers.Location.ToString());
        }


        private static (MyCookieAuthenticationEvents Events, DefaultHttpContext Context) Build(string path)
        {
            var events = new MyCookieAuthenticationEvents(new Mock<IUserManager>().Object);
            var context = new DefaultHttpContext();
            context.Request.Path = path;

            return (events, context);
        }

        private static RedirectContext<CookieAuthenticationOptions> BuildRedirectContext(HttpContext context)
        {
            var scheme = new AuthenticationScheme(
                CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));

            return new RedirectContext<CookieAuthenticationOptions>(
                context, scheme, new CookieAuthenticationOptions { LoginPath = "/Account/Index" },
                new AuthenticationProperties(), "/Account/Index");
        }
    }
}
