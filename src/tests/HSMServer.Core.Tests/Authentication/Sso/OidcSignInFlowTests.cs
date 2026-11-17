using HSMServer.Authentication;
using HSMServer.Authentication.Sso;
using HSMServer.Model.Authentication;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.Sso
{
    // The whole browser round trip of an SSO sign-in (#1782) through the real OIDC handler,
    // with an in-process stand-in for the provider: challenge with PKCE, callback, code
    // exchange, id_token and userinfo validation, then the HSM auth cookie. Pins what unit
    // tests of the options cannot: that the claim names the provider sends (no inbound
    // renaming) reach the sign-in service, and that the issued cookie authenticates.
    public sealed class OidcSignInFlowTests : IAsyncLifetime
    {
        private const string Issuer = "https://sso.example.test/application/o/hsm/";
        private const string ClientId = "hsm";
        private const string ClientSecret = "client-secret-for-tests";
        private const string Code = "authorization-code";
        private const string Subject = "8f3c0d2a";

        private readonly RsaSecurityKey _signingKey = new(RSA.Create(2048)) { KeyId = "test-key" };
        private readonly Dictionary<string, User> _users = new();
        private readonly FakeProvider _provider;

        private IHost _host;
        private HttpClient _client;

        private string _nonce;


        public OidcSignInFlowTests()
        {
            _provider = new FakeProvider(this);
        }


        public async Task InitializeAsync()
        {
            var userManager = new Mock<IUserManager>();
            userManager.Setup(m => m[It.IsAny<string>()]).Returns((string name) => _users.GetValueOrDefault(name));
            userManager.Setup(m => m.TryAdd(It.IsAny<User>())).Returns((User user) => Task.FromResult(_users.TryAdd(user.Name, user)));
            userManager.Setup(m => m.TryUpdate(It.IsAny<UserUpdate>())).Returns((UserUpdate update) =>
            {
                _users.Values.Single(u => u.Id == update.Id).Update(update);
                return Task.FromResult(true);
            });

            var config = new OidcConfig
            {
                Enabled = true,
                Authority = Issuer,
                ClientId = ClientId,
                ClientSecret = ClientSecret,
                AdminGroup = "hsm-admins",
                SessionLifetime = TimeSpan.FromHours(12),
            };
            Assert.Empty(config.Validate());

            _host = await Host.CreateDefaultBuilder()
                .UseEnvironment(Environments.Development)
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddSingleton(userManager.Object);
                        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                                .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
                                .AddHsmOidc(config);

                        // The provider's discovery document and back channel, in process.
                        services.PostConfigure<OpenIdConnectOptions>(HsmOidcDefaults.AuthenticationScheme, options =>
                        {
                            var discovery = new OpenIdConnectConfiguration
                            {
                                Issuer = Issuer,
                                AuthorizationEndpoint = "https://sso.example.test/application/o/authorize/",
                                TokenEndpoint = "https://sso.example.test/application/o/token/",
                                UserInfoEndpoint = "https://sso.example.test/application/o/userinfo/",
                            };
                            discovery.SigningKeys.Add(_signingKey);

                            options.ConfigurationManager = new FixedDiscovery(discovery);
                            options.Backchannel = new HttpClient(_provider);
                        });
                    });
                    web.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/login", () => Results.Challenge(
                                new AuthenticationProperties { RedirectUri = HsmOidcDefaults.AfterSignInPath },
                                [HsmOidcDefaults.AuthenticationScheme]));

                            endpoints.MapGet("/whoami", (HttpContext context) =>
                                context.User.Identity?.IsAuthenticated == true ? Results.Text(context.User.Identity.Name) : Results.Unauthorized());
                        });
                    });
                })
                .StartAsync();

            _host.GetTestServer().BaseAddress = new Uri("https://hsm.example.test/");
            _client = _host.GetTestClient();
        }

        public async Task DisposeAsync()
        {
            _client?.Dispose();

            if (_host is not null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }


        [Fact]
        public async Task FullRoundTrip_SignsTheProviderUsernameIn_AndAppliesTheAdminGroup()
        {
            _provider.Groups = ["logs-readers", "hsm-admins"];

            var cookie = await SignIn();

            var whoami = new HttpRequestMessage(HttpMethod.Get, "/whoami");
            whoami.Headers.Add("Cookie", cookie);
            var response = await _client.SendAsync(whoami);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("ivanov", await response.Content.ReadAsStringAsync());

            var created = Assert.Single(_users.Values);
            Assert.Equal("ivanov", created.Name);
            Assert.True(created.IsAdmin);
            Assert.True(string.IsNullOrEmpty(created.Password));
        }

        [Fact]
        public async Task Challenge_UsesPkceAndTheConfiguredScopes()
        {
            var challenge = await _client.GetAsync("/login");

            Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);

            var location = challenge.Headers.Location;
            var query = QueryHelpers.ParseQuery(location.Query);

            Assert.StartsWith("https://sso.example.test/application/o/authorize/", location.ToString());
            Assert.Equal(ClientId, query["client_id"]);
            Assert.Equal("code", query["response_type"]);
            Assert.Equal("S256", query["code_challenge_method"]);
            Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
            Assert.Equal("https://hsm.example.test/signin-oidc", query["redirect_uri"]);
            Assert.Equal(["openid", "profile"], query["scope"].ToString().Split(' ').Order());
        }

        [Fact]
        public async Task IssuedCookie_HasTheAbsoluteSessionLifetime()
        {
            var before = DateTimeOffset.UtcNow;

            var setCookie = await SignInSetCookieHeader();

            var expiresText = setCookie.Split(';').Select(p => p.Trim())
                                       .Single(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase))["expires=".Length..];
            var expires = DateTimeOffset.Parse(expiresText);

            Assert.InRange(expires, before.AddHours(12).AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(12).AddMinutes(1));
        }

        [Fact]
        public async Task MissingUsernameClaim_ReturnsToTheLoginPageWithoutACookie()
        {
            _provider.Username = null;

            var (callback, _) = await Callback();

            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            Assert.Equal("/Account/Index?ssoError=MissingUsername", callback.Headers.Location?.ToString());
            Assert.DoesNotContain(SetCookies(callback), c => c.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));
            Assert.Empty(_users);
        }

        [Fact]
        public async Task ProviderError_ReturnsToTheLoginPage()
        {
            var challenge = await _client.GetAsync("/login");
            var state = QueryHelpers.ParseQuery(challenge.Headers.Location.Query)["state"].ToString();

            var request = new HttpRequestMessage(HttpMethod.Post, "/signin-oidc")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["error"] = "access_denied", ["state"] = state }),
            };
            request.Headers.Add("Cookie", CookieHeader(challenge));

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Account/Index?ssoError=ProviderFailure", response.Headers.Location?.ToString());
        }


        private async Task<string> SignIn() => (await SignInSetCookieHeader()).Split(';')[0];

        private async Task<string> SignInSetCookieHeader()
        {
            var (callback, _) = await Callback();

            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            Assert.Equal("/Home", callback.Headers.Location?.ToString());

            return SetCookies(callback).Single(c => c.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));
        }

        // The browser's part: follow the challenge to the provider, come back to the callback
        // with the code and the state, carrying the correlation and nonce cookies.
        private async Task<(HttpResponseMessage Callback, HttpResponseMessage Challenge)> Callback()
        {
            var challenge = await _client.GetAsync("/login");
            var query = QueryHelpers.ParseQuery(challenge.Headers.Location.Query);

            _nonce = query["nonce"];
            _provider.ExpectedChallenge = query["code_challenge"];

            var request = new HttpRequestMessage(HttpMethod.Post, "/signin-oidc")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = Code, ["state"] = query["state"] }),
            };
            request.Headers.Add("Cookie", CookieHeader(challenge));

            return (await _client.SendAsync(request), challenge);
        }

        private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
            response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

        private static string CookieHeader(HttpResponseMessage response) =>
            string.Join("; ", SetCookies(response).Select(c => c.Split(';')[0]));


        private string IdToken(FakeProvider provider)
        {
            var claims = new Dictionary<string, object>
            {
                ["sub"] = Subject,
                ["nonce"] = _nonce,
                ["groups"] = provider.Groups,
            };

            if (provider.Username is not null)
                claims["preferred_username"] = provider.Username;

            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = ClientId,
                IssuedAt = DateTime.UtcNow,
                NotBefore = DateTime.UtcNow.AddMinutes(-1),
                Expires = DateTime.UtcNow.AddMinutes(5),
                Claims = claims,
                SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
            });
        }


        private sealed class FixedDiscovery(OpenIdConnectConfiguration configuration) : IConfigurationManager<OpenIdConnectConfiguration>
        {
            public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) => Task.FromResult(configuration);

            public void RequestRefresh() { }
        }

        // The provider's token and userinfo endpoints. The token endpoint checks what a real
        // provider checks of the client: the code, the client credentials and the PKCE verifier.
        private sealed class FakeProvider(OidcSignInFlowTests test) : HttpMessageHandler
        {
            public string Username { get; set; } = "ivanov";

            public string[] Groups { get; set; } = [];

            public string ExpectedChallenge { get; set; }


            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/token/"))
                {
                    var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));

                    Assert.Equal("authorization_code", form["grant_type"]);
                    Assert.Equal(Code, form["code"]);
                    Assert.Equal(ClientId, form["client_id"]);
                    Assert.Equal(ClientSecret, form["client_secret"]);
                    Assert.Equal(ExpectedChallenge, Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]))));

                    return Json(new { access_token = "access-token", token_type = "Bearer", expires_in = 300, id_token = test.IdToken(this) });
                }

                if (request.RequestUri.AbsolutePath.EndsWith("/userinfo/"))
                {
                    var userInfo = new Dictionary<string, object> { ["sub"] = Subject, ["groups"] = Groups };
                    if (Username is not null)
                        userInfo["preferred_username"] = Username;

                    return Json(userInfo);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            private static HttpResponseMessage Json(object body) =>
                new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        }
    }
}
