using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMServer.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // Scheme isolation contract (initiative step 3): cookie keeps both defaults and stays
    // the scheme behind bare [Authorize]; HsmApiToken exists only for the explicit
    // management policy; that policy accepts exactly one HsmApiToken identity and rejects
    // cookie-only and mixed principals.
    public class HsmApiTokenSchemeIsolationTests
    {
        private static readonly Guid OwnerId = Guid.NewGuid();
        private static readonly string TokenId = new('A', ApiTokenMaterial.TokenIdLength);


        private static IServiceProvider BuildProvider()
        {
            var services = new ServiceCollection();

            services.AddLogging();
            services.AddSingleton(new Moq.Mock<IApiTokenManager>().Object);
            services.AddSingleton(new Moq.Mock<IUserManager>().Object);
            services.AddSingleton(new Moq.Mock<IApiTokenSecurityEventSink>().Object);

            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddHsmApiTokenScheme()
                .Services
                .AddHsmApiTokenAuthorization();

            return services.BuildServiceProvider();
        }

        private static ClaimsPrincipal TokenPrincipal() => new(new ClaimsIdentity(
            authenticationType: HsmApiTokenDefaults.AuthenticationScheme,
            claims:
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, OwnerId.ToString()),
                new Claim(HsmApiTokenClaims.TokenId, TokenId),
            ]));

        private static ClaimsPrincipal CookiePrincipal() => new(new ClaimsIdentity(
            CookieAuthenticationDefaults.AuthenticationScheme));


        [Fact]
        public async Task Cookie_RemainsDefaultAuthenticateAndChallengeScheme()
        {
            var provider = BuildProvider();
            var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

            Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme,
                (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name);
            Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme,
                (await schemes.GetDefaultChallengeSchemeAsync())?.Name);
        }

        [Fact]
        public void DefaultPolicy_IsPinnedToTheCookieSchemeOnly()
        {
            // Bare [Authorize] on legacy MVC/Razor must resolve through cookie only, so an
            // API-token identity can never satisfy a legacy authorization.
            var provider = BuildProvider();
            var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

            var policy = options.DefaultPolicy;

            var scheme = Assert.Single(policy.AuthenticationSchemes);
            Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, scheme);
        }

        [Fact]
        public async Task HsmApiTokenScheme_IsRegisteredButNeverDefault()
        {
            var provider = BuildProvider();
            var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

            var scheme = await schemes.GetSchemeAsync(HsmApiTokenDefaults.AuthenticationScheme);
            Assert.NotNull(scheme);
            Assert.Equal(typeof(HsmApiTokenHandler), scheme.HandlerType);
            Assert.NotEqual(HsmApiTokenDefaults.AuthenticationScheme,
                (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name);
        }

        [Fact]
        public async Task ManagementPolicy_RejectsCookieOnlyPrincipal()
        {
            var provider = BuildProvider();
            var authorization = provider.GetRequiredService<IAuthorizationService>();

            var result = await authorization.AuthorizeAsync(CookiePrincipal(), null,
                HsmApiTokenDefaults.ManagementPolicy);

            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task ManagementPolicy_AcceptsSingleTokenIdentity()
        {
            var provider = BuildProvider();
            var authorization = provider.GetRequiredService<IAuthorizationService>();

            var result = await authorization.AuthorizeAsync(TokenPrincipal(), null,
                HsmApiTokenDefaults.ManagementPolicy);

            Assert.True(result.Succeeded);
        }

        [Fact]
        public async Task ManagementPolicy_FailsClosedOnMixedIdentities()
        {
            // Cookie + token identities on one principal must never widen access.
            var mixed = new ClaimsPrincipal([TokenPrincipal().Identities.Single(), CookiePrincipal().Identities.Single()]);

            var provider = BuildProvider();
            var authorization = provider.GetRequiredService<IAuthorizationService>();

            var result = await authorization.AuthorizeAsync(mixed, null,
                HsmApiTokenDefaults.ManagementPolicy);

            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task ManagementPolicy_RejectsForeignIdentityClaimingOurSchemeName()
        {
            // An identity whose authentication type merely looks like ours but carries no
            // owner claim is not a principal this handler produced.
            var forged = new ClaimsPrincipal(new ClaimsIdentity(HsmApiTokenDefaults.AuthenticationScheme));

            var provider = BuildProvider();
            var authorization = provider.GetRequiredService<IAuthorizationService>();

            var result = await authorization.AuthorizeAsync(forged, null,
                HsmApiTokenDefaults.ManagementPolicy);

            Assert.False(result.Succeeded);
        }
    }
}
