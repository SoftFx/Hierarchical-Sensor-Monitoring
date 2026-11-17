using HSMServer.Authentication;
using HSMServer.Authentication.Sso;
using HSMServer.Controllers;
using HSMServer.Model.ViewModel;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Moq;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.Sso
{
    // The wiring of the optional OIDC sign-in (#1782): with the section off the server has
    // exactly the schemes it had before; with it on, the scheme runs the code flow with PKCE
    // and turns the provider's ticket into the usual HSM cookie with an absolute lifetime.
    public class HsmOidcTests
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);


        private static OidcConfig Active(Action<OidcConfig> change = null)
        {
            var config = new OidcConfig
            {
                Enabled = true,
                Authority = "https://sso.example.test/application/o/hsm/",
                ClientId = "hsm",
                ClientSecret = "secret",
                DisplayName = "Authentik",
            };

            change?.Invoke(config);
            Assert.Empty(config.Validate());

            return config;
        }

        private static ServiceProvider BuildServices(OidcConfig config)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(new Mock<IUserManager>().Object);
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddHsmOidc(config);

            return services.BuildServiceProvider();
        }


        [Fact]
        public async Task Off_RegistersNoOidcScheme()
        {
            var config = new OidcConfig();
            config.Validate();

            using var services = BuildServices(config);
            var schemes = services.GetRequiredService<IAuthenticationSchemeProvider>();

            Assert.Null(await schemes.GetSchemeAsync(HsmOidcDefaults.AuthenticationScheme));
            Assert.Equal([CookieAuthenticationDefaults.AuthenticationScheme], (await schemes.GetAllSchemesAsync()).Select(s => s.Name));
            Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, (await schemes.GetDefaultChallengeSchemeAsync())?.Name);
        }

        [Fact]
        public async Task EnabledButIncomplete_RegistersNoOidcScheme()
        {
            var config = new OidcConfig { Enabled = true, Authority = "https://sso.example.test/", ClientId = "hsm" };
            Assert.NotEmpty(config.Validate());       // no client secret

            using var services = BuildServices(config);

            Assert.Null(await services.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(HsmOidcDefaults.AuthenticationScheme));
        }

        [Fact]
        public async Task On_RegistersTheScheme_WithoutChangingTheDefaultChallenge()
        {
            using var services = BuildServices(Active());
            var schemes = services.GetRequiredService<IAuthenticationSchemeProvider>();

            var oidc = await schemes.GetSchemeAsync(HsmOidcDefaults.AuthenticationScheme);

            Assert.NotNull(oidc);
            Assert.Equal("Authentik", oidc.DisplayName);
            // An unauthenticated browser still lands on the HSM login page, not at the provider.
            Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, (await schemes.GetDefaultChallengeSchemeAsync())?.Name);
        }

        [Fact]
        public void On_UsesTheCodeFlowWithPkce_AndTheConfiguredValues()
        {
            using var services = BuildServices(Active(c =>
            {
                c.Scopes = "openid profile email";
                c.UsernameClaim = "email";
                c.GroupsClaim = "roles";
            }));

            var options = services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(HsmOidcDefaults.AuthenticationScheme);

            Assert.Equal("https://sso.example.test/application/o/hsm/", options.Authority);
            Assert.Equal("hsm", options.ClientId);
            Assert.Equal("secret", options.ClientSecret);
            Assert.Equal(OpenIdConnectResponseType.Code, options.ResponseType);
            Assert.True(options.UsePkce);
            Assert.False(options.SaveTokens);
            Assert.True(options.RequireHttpsMetadata);
            Assert.False(options.MapInboundClaims);
            Assert.True(options.GetClaimsFromUserInfoEndpoint);
            Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, options.SignInScheme);
            Assert.Equal(new PathString("/signin-oidc"), options.CallbackPath);
            Assert.True(options.Scope.ToHashSet().SetEquals(["openid", "profile", "email"]));
            Assert.Equal("email", options.TokenValidationParameters.NameClaimType);
            Assert.Equal(typeof(HsmOidcEvents), options.EventsType);
            Assert.Contains(options.ClaimActions, action => action.ClaimType == "email");
            Assert.Contains(options.ClaimActions, action => action.ClaimType == "roles");
        }


        [Fact]
        public async Task TicketReceived_IssuesTheHsmCookiePrincipal_WithAnAbsoluteLifetime()
        {
            var config = Active(c => c.SessionLifetime = TimeSpan.FromHours(8));
            var (events, context) = BuildTicket(config, SsoSignInResult.Success("ivanov"));

            await events.TicketReceived(context);

            Assert.False(context.Result?.Handled ?? false);

            // Only the HSM login travels on, in the same shape as a password sign-in.
            var claim = Assert.Single(context.Principal.Claims);
            Assert.Equal(ClaimsIdentity.DefaultNameClaimType, claim.Type);
            Assert.Equal("ivanov", context.Principal.Identity.Name);

            Assert.True(context.Properties.IsPersistent);
            Assert.Equal(Now, context.Properties.IssuedUtc);
            Assert.Equal(Now.AddHours(8), context.Properties.ExpiresUtc);
            Assert.False(context.Properties.AllowRefresh);
            Assert.Equal("/Home", context.Properties.RedirectUri);
        }

        [Theory]
        [InlineData(SsoSignInError.UnknownUser)]
        [InlineData(SsoSignInError.MissingUsername)]
        [InlineData(SsoSignInError.CreateFailed)]
        public async Task TicketReceived_Rejected_SignsNothingIn_AndReturnsToTheLoginPage(SsoSignInError error)
        {
            var (events, context) = BuildTicket(Active(), SsoSignInResult.Fail(error));

            await events.TicketReceived(context);

            Assert.True(context.Result.Handled);
            Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
            Assert.Equal($"/Account/Index?ssoError={error}", context.Response.Headers.Location.ToString());
        }

        [Fact]
        public async Task RemoteFailure_ReturnsToTheLoginPage()
        {
            var config = Active();
            var events = new HsmOidcEvents(Mock.Of<ISsoSignInService>(), config, NullLogger<HsmOidcEvents>.Instance, new FixedTime());
            var http = new DefaultHttpContext();
            var context = new RemoteFailureContext(http, Scheme(), new OpenIdConnectOptions(), new Exception("access_denied"));

            await events.RemoteFailure(context);

            Assert.True(context.Result.Handled);
            Assert.Equal("/Account/Index?ssoError=ProviderFailure", http.Response.Headers.Location.ToString());
        }


        [Fact]
        public void LoginPage_ShowsTheButtonOnlyWhenSsoIsOn()
        {
            var off = new OidcConfig();
            off.Validate();

            Assert.Null(LoginModel(off).SsoDisplayName);
            Assert.Equal("Authentik", LoginModel(Active()).SsoDisplayName);
        }

        [Fact]
        public void LoginPage_ExplainsAFailedSsoSignIn()
        {
            var controller = Controller(Active());

            var model = (LoginViewModel)((ViewResult)controller.Index(SsoSignInError.UnknownUser)).Model;

            Assert.Contains("no HSM user", model.SsoError);
            Assert.Null(((LoginViewModel)((ViewResult)controller.Index()).Model).SsoError);
        }

        [Fact]
        public async Task SsoAction_IsNotFound_WhenSsoIsOff()
        {
            var off = new OidcConfig();
            off.Validate();

            Assert.IsType<NotFoundResult>(await Controller(off).Sso());
        }

        [Fact]
        public async Task SsoAction_ChallengesTheOidcScheme_WhenSsoIsOn()
        {
            var authentication = new Mock<IAuthenticationService>();
            var controller = Controller(Active(), authentication.Object);

            Assert.IsType<EmptyResult>(await controller.Sso());

            authentication.Verify(a => a.ChallengeAsync(It.IsAny<HttpContext>(), HsmOidcDefaults.AuthenticationScheme,
                It.Is<AuthenticationProperties>(p => p.RedirectUri == "/Home")), Times.Once);
        }

        [Fact]
        public async Task SsoAction_UnreachableProvider_ReturnsToThePasswordForm()
        {
            // What the OIDC handler throws when the discovery document cannot be fetched.
            var authentication = new Mock<IAuthenticationService>();
            authentication.Setup(a => a.ChallengeAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()))
                          .ThrowsAsync(new InvalidOperationException("IDX20803: Unable to obtain configuration"));

            var result = Assert.IsType<RedirectResult>(await Controller(Active(), authentication.Object).Sso());

            Assert.Equal("/Account/Index?ssoError=ProviderUnavailable", result.Url);
            Assert.Contains("cannot be reached", HsmOidcDefaults.Describe(SsoSignInError.ProviderUnavailable));
        }


        private static AccountController Controller(OidcConfig config, IAuthenticationService authentication = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(authentication ?? Mock.Of<IAuthenticationService>());
            services.AddSingleton(Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataDictionaryFactory>(
                f => f.GetTempData(It.IsAny<HttpContext>()) == Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataDictionary>()));

            return new AccountController(Mock.Of<IUserManager>(), null, config, NullLogger<AccountController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() } },
            };
        }

        private static LoginViewModel LoginModel(OidcConfig config) =>
            (LoginViewModel)((ViewResult)Controller(config).Index()).Model;

        private static AuthenticationScheme Scheme() =>
            new(HsmOidcDefaults.AuthenticationScheme, "Authentik", typeof(OpenIdConnectHandler));

        private static (HsmOidcEvents, TicketReceivedContext) BuildTicket(OidcConfig config, SsoSignInResult result)
        {
            var signIn = new Mock<ISsoSignInService>();
            signIn.Setup(s => s.ResolveUser(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(result);

            var events = new HsmOidcEvents(signIn.Object, config, NullLogger<HsmOidcEvents>.Instance, new FixedTime());

            var external = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "a1b2c3"),
                                                                   new Claim("preferred_username", "ivanov"),
                                                                   new Claim("groups", "logs-readers")], "Oidc"));

            var ticket = new AuthenticationTicket(external, new AuthenticationProperties(), HsmOidcDefaults.AuthenticationScheme);
            var context = new TicketReceivedContext(new DefaultHttpContext(), Scheme(), new OpenIdConnectOptions(), ticket);

            return (events, context);
        }

        private sealed class FixedTime : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
