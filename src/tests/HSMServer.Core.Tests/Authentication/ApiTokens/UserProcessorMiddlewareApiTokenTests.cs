using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Middleware;
using HSMServer.Model.Authentication;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // UserProcessorMiddleware replaces context.User with the stored HSM user selected by
    // Identity.Name on the SitePort listener. A token principal has no Name and must never
    // be replaced: the middleware short-circuits for exactly the HsmApiToken identity, so
    // authorization still sees the token principal with its owner/token claims (initiative
    // step 3: "principal replacement can restore unrestricted owner rights").
    public class UserProcessorMiddlewareApiTokenTests
    {
        private const int SitePort = 44333;

        private static readonly Guid OwnerId = Guid.NewGuid();


        [Fact]
        public async Task TokenPrincipal_IsPassedThroughUnchanged()
        {
            // Strict mock: any attempt to resolve a user by name would prove the
            // middleware failed to short-circuit.
            var userManager = new Mock<IUserManager>(MockBehavior.Strict);
            userManager.Setup(u => u[It.IsAny<string>()])
                .Throws(new InvalidOperationException("token principals must not be user-resolved"));

            var principal = TokenPrincipal();
            var context = SitePortContext(principal, userManager.Object);

            await new UserProcessorMiddleware(NextThatMarks, userManager.Object, Config()).InvokeAsync(context);

            Assert.Same(principal, context.User);
            Assert.True((bool)context.Items["reached"]);
        }

        [Fact]
        public async Task CookiePrincipal_IsStillReplacedByStoredUser()
        {
            var stored = new User("alice");
            var userManager = new Mock<IUserManager>();
            userManager.Setup(u => u["alice"]).Returns(stored);

            var context = SitePortContext(CookiePrincipal("alice"), userManager.Object);

            await new UserProcessorMiddleware(_ => Task.CompletedTask, userManager.Object, Config()).InvokeAsync(context);

            Assert.Same(stored, context.User);
        }

        [Fact]
        public async Task TokenIdentityNotFirst_IsStillPassedThrough()
        {
            // The short-circuit must not depend on the token identity being the primary
            // one: a merged principal that happens to order a cookie identity first still
            // carries the token identity, and replacing the principal would restore the
            // owner's unrestricted rights behind the token's grants.
            var userManager = new Mock<IUserManager>(MockBehavior.Strict);
            userManager.Setup(u => u[It.IsAny<string>()])
                .Throws(new InvalidOperationException("token principals must not be user-resolved"));

            var principal = new ClaimsPrincipal(new[]
            {
                (ClaimsIdentity)CookiePrincipal("alice").Identity,
                (ClaimsIdentity)TokenPrincipal().Identity,
            });
            var context = SitePortContext(principal, userManager.Object);

            await new UserProcessorMiddleware(NextThatMarks, userManager.Object, Config()).InvokeAsync(context);

            Assert.Same(principal, context.User);
            Assert.True((bool)context.Items["reached"]);
        }


        private static IServerConfig Config()
        {
            var config = new Mock<IServerConfig>();
            config.SetupGet(c => c.Kestrel).Returns(new KestrelConfig { SitePort = SitePort });
            return config.Object;
        }

        private static DefaultHttpContext SitePortContext(ClaimsPrincipal user, IUserManager userManager)
        {
            var context = new DefaultHttpContext { User = user };
            context.Features.Set<IHttpConnectionFeature>(new FixedPortConnectionFeature(SitePort));
            context.RequestServices = new ServiceCollection()
                .AddSingleton(userManager)
                .BuildServiceProvider();
            return context;
        }

        private static ClaimsPrincipal TokenPrincipal() => new(new ClaimsIdentity(
            authenticationType: HsmApiTokenDefaults.AuthenticationScheme,
            claims:
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, OwnerId.ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ]));

        private static ClaimsPrincipal CookiePrincipal(string name) => new(
            new ClaimsIdentity(authenticationType: "Cookies", claims: [new Claim(ClaimTypes.Name, name)]));

        private static Task NextThatMarks(HttpContext context)
        {
            context.Items["reached"] = true;
            return Task.CompletedTask;
        }


        private sealed class FixedPortConnectionFeature : IHttpConnectionFeature
        {
            public FixedPortConnectionFeature(int localPort) => LocalPort = localPort;

            public string ConnectionId { get; set; }
            public IPAddress LocalIpAddress { get; set; }
            public int LocalPort { get; set; }
            public IPAddress RemoteIpAddress { get; set; }
            public int RemotePort { get; set; }
        }
    }
}
