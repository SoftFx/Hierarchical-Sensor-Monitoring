using System;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // HTTP contract of the HsmApiToken authentication handler: bearer-only input, the manager
    // as the single authentication decision, owner existence, a minimal single-identity
    // principal, and one indistinguishable generic failure for every rejection path.
    public class HsmApiTokenHandlerTests
    {
        private const string Scheme = HsmApiTokenDefaults.AuthenticationScheme;

        private static readonly Guid OwnerId = Guid.NewGuid();
        private static readonly Guid EntityId = Guid.NewGuid();
        private static readonly string TokenId = new('A', ApiTokenMaterial.TokenIdLength);

        private readonly Mock<IApiTokenManager> _managerMock = new();
        private readonly Mock<IUserManager> _usersMock = new();
        private readonly Mock<IApiTokenSecurityEventSink> _securityEvents = new();

        // Real limiter, effectively unlimited by default so existing tests are about the
        // handler contract, not the budget; the limiter has its own test file. Tests that
        // need the budget to persist ACROSS authentications share one instance here.
        private int _invalidAttemptLimit = int.MaxValue;
        private ApiTokenInvalidAttemptLimiter _limiterOverride;

        private readonly User _owner = new("owner") { Id = OwnerId };


        public HsmApiTokenHandlerTests()
        {
            _usersMock.Setup(u => u[OwnerId]).Returns(_owner);
        }

        private static ApiTokenInfo BuildInfo() => new()
        {
            EntityId = EntityId,
            OwnerUserId = OwnerId,
            Name = "token",
        };

        private static string ValidCredential() =>
            ApiTokenMaterial.FormatToken(TokenId, new('B', ApiTokenMaterial.SecretLength));

        private void SetupManagerAccepts(string credential, ApiTokenInfo info)
        {
            _managerMock
                .Setup(m => m.TryAuthenticate(credential, out It.Ref<ApiTokenInfo>.IsAny))
                .Callback(new TryAuthenticateOutCallback((string _, out ApiTokenInfo outInfo) => outInfo = info))
                .Returns(info is not null);
        }


        private async Task<AuthenticateResult> AuthenticateAsync(string authorizationHeader,
            string remoteIp = null, int? remotePort = null)
        {
            var (provider, context) = Build(authorizationHeader);

            if (remoteIp is not null)
            {
                context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);

                if (remotePort is not null)
                    context.Connection.RemotePort = remotePort.Value;
            }

            return await provider.GetRequiredService<IAuthenticationService>()
                .AuthenticateAsync(context, Scheme);
        }

        private async Task<HttpContext> ChallengeAsync(string authorizationHeader = null)
        {
            var (provider, context) = Build(authorizationHeader);

            await provider.GetRequiredService<IAuthenticationService>()
                .ChallengeAsync(context, Scheme, new AuthenticationProperties());

            return context;
        }

        private (IServiceProvider Provider, HttpContext Context) Build(string authorizationHeader)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(_managerMock.Object);
            services.AddSingleton(_usersMock.Object);
            services.AddSingleton(_securityEvents.Object);
            services.AddSingleton(_limiterOverride ?? new ApiTokenInvalidAttemptLimiter(
                new ServerConfiguration.ApiTokensConfig { InvalidAttemptRateLimit = _invalidAttemptLimit },
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ApiTokenInvalidAttemptLimiter>.Instance));
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, HsmApiTokenHandler>(Scheme, _ => { });

            var provider = services.BuildServiceProvider();

            var context = new DefaultHttpContext { RequestServices = provider.CreateScope().ServiceProvider };

            // DefaultHttpContext writes into Stream.Null — capture the body for the
            // error-contract assertions (challenge test parses what the handler wrote).
            context.Response.Body = new System.IO.MemoryStream();

            if (authorizationHeader is not null)
                context.Request.Headers.Authorization = authorizationHeader;

            return (provider, context);
        }


        [Fact]
        public async Task ValidBearer_AuthenticatesWithSingleMinimalIdentity()
        {
            var credential = ValidCredential();
            SetupManagerAccepts(credential, BuildInfo());

            var result = await AuthenticateAsync($"Bearer {credential}");

            Assert.True(result.Succeeded);
            var principal = result.Principal;
            Assert.NotNull(principal);
            var identity = Assert.Single(principal.Identities);
            Assert.True(identity.IsAuthenticated);
            Assert.Equal(Scheme, identity.AuthenticationType);

            // Minimal by design: owner id + token id, never a stored user login as Name.
            Assert.Null(identity.Name);
            Assert.Equal(OwnerId.ToString(), principal.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value);
            Assert.Equal(TokenId, principal.FindFirst(HsmApiTokenClaims.TokenId)?.Value);
        }

        [Fact]
        public async Task MissingAuthorizationHeader_IsNotThisSchemesCredential()
        {
            var result = await AuthenticateAsync(null);

            Assert.False(result.Succeeded);
            Assert.True(result.None);
        }

        [Theory]
        [InlineData("Basic dXNlcjpwYXNz")]        // another scheme, clearly not ours
        [InlineData("Bearer")]                    // bearer without a credential
        [InlineData("Bearer some_other_token")]   // bearer of a foreign format
        public async Task ForeignCredential_IsIgnoredWithoutManagerLookup(string header)
        {
            var result = await AuthenticateAsync(header);

            Assert.True(result.None);
            _managerMock.Verify(m => m.TryAuthenticate(It.IsAny<string>(), out It.Ref<ApiTokenInfo>.IsAny), Times.Never);
        }

        [Fact]
        public async Task DuplicatedAuthorizationValues_AreNotThisSchemesCredential()
        {
            // Duplicated Authorization headers joined with ", " would parse as the FIRST
            // value's scheme ("Basic") and hide the bearer. The ambiguous shape is treated
            // as no credential — and never reaches the manager.
            var (provider, context) = Build(null);
            context.Request.Headers.Append("Authorization", "Basic dXNlcjpwYXNz");
            context.Request.Headers.Append("Authorization", $"Bearer {ValidCredential()}");

            var result = await provider.GetRequiredService<IAuthenticationService>()
                .AuthenticateAsync(context, Scheme);

            Assert.True(result.None);
            _managerMock.Verify(m => m.TryAuthenticate(It.IsAny<string>(), out It.Ref<ApiTokenInfo>.IsAny), Times.Never);
        }

        [Theory]
        [InlineData("hsm_pat_v1_short.secret")]                                                       // too short overall
        [InlineData("hsm_pat_v1_" + "AAAAAAAAAAAAAAAAAAAAAA")]                                       // no dot/secret
        [InlineData("hsm_pat_v1_" + "AAAAAAAAAAAAAAAAAAAAAA." + "B!B")]                              // bad alphabet
        [InlineData("hsm_pat_v1_" + "AAAAAAAAAAAAAAAAAAAAAA." + "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")]   // secret too long
        public async Task MalformedHsmBearer_FailsClosedWithoutManagerLookup(string credential)
        {
            var result = await AuthenticateAsync($"Bearer {credential}");

            Assert.False(result.Succeeded);
            Assert.False(result.None);
            _managerMock.Verify(m => m.TryAuthenticate(It.IsAny<string>(), out It.Ref<ApiTokenInfo>.IsAny), Times.Never);
        }

        [Fact]
        public async Task ManagerRejects_FailsClosed()
        {
            var credential = ValidCredential();
            SetupManagerAccepts(credential, info: null);

            var result = await AuthenticateAsync($"Bearer {credential}");

            Assert.False(result.Succeeded);
            Assert.False(result.None);
        }

        [Fact]
        public async Task NonCanonicalId_FailureEventCarriesNoTokenId()
        {
            // The cheap shape check (length/prefix/dot) passes while the id alphabet is
            // attacker-chosen. The failure event still records the attempt — but with a
            // null TokenId: an unauthenticated caller must not get a write channel of
            // arbitrary bytes into the append-only security store.
            var credential = ApiTokenMaterial.TokenPrefix + new string('!', ApiTokenMaterial.TokenIdLength) +
                "." + new string('B', ApiTokenMaterial.SecretLength);

            var result = await AuthenticateAsync($"Bearer {credential}");

            Assert.False(result.Succeeded);
            Assert.False(result.None);
            _securityEvents.Verify(s => s.Record(It.Is<ApiTokenSecurityEvent>(e =>
                e.Kind == ApiTokenSecurityEventKind.AuthFailed && e.TokenId == null)), Times.Once);
        }

        [Fact]
        public async Task CanonicalId_AuthenticationFailure_RecordsThePublicTokenId()
        {
            var credential = ValidCredential();
            SetupManagerAccepts(credential, info: null);

            await AuthenticateAsync($"Bearer {credential}");

            _securityEvents.Verify(s => s.Record(It.Is<ApiTokenSecurityEvent>(e =>
                e.Kind == ApiTokenSecurityEventKind.AuthFailed && e.TokenId == TokenId)), Times.Once);
        }

        [Fact]
        public async Task FailuresOverThePerSourceBudget_EventIsDropped_AuthenticationUnchanged()
        {
            // Both attempts come from the same (null-remote-endpoint) source: with a
            // budget of 1 only the first failure event is recorded — the second is
            // dropped by the limiter, and the authentication RESULT is identical.
            _limiterOverride = new ApiTokenInvalidAttemptLimiter(
                new ServerConfiguration.ApiTokensConfig { InvalidAttemptRateLimit = 1 },
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ApiTokenInvalidAttemptLimiter>.Instance);
            var credential = ValidCredential();
            SetupManagerAccepts(credential, info: null);

            var first = await AuthenticateAsync($"Bearer {credential}");
            var second = await AuthenticateAsync($"Bearer {credential}");

            Assert.False(first.Succeeded);
            Assert.False(second.Succeeded);
            _securityEvents.Verify(s => s.Record(It.Is<ApiTokenSecurityEvent>(e =>
                e.Kind == ApiTokenSecurityEventKind.AuthFailed)), Times.Once);
        }

        [Fact]
        public async Task FailuresFromOneIp_EphemeralPortsShareOneBudget()
        {
            // The budget identity is the remote IP: the port is the client's ephemeral
            // port (fresh per TCP connection), so bucketing on ip:port would hand every
            // connection a brand-new budget. The full ip:port stays in the payload.
            _limiterOverride = new ApiTokenInvalidAttemptLimiter(
                new ServerConfiguration.ApiTokensConfig { InvalidAttemptRateLimit = 1 },
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ApiTokenInvalidAttemptLimiter>.Instance);
            var credential = ValidCredential();
            SetupManagerAccepts(credential, info: null);

            var first = await AuthenticateAsync($"Bearer {credential}", remoteIp: "10.0.0.5", remotePort: 11111);
            var second = await AuthenticateAsync($"Bearer {credential}", remoteIp: "10.0.0.5", remotePort: 22222);

            Assert.False(first.Succeeded);
            Assert.False(second.Succeeded);
            _securityEvents.Verify(s => s.Record(It.Is<ApiTokenSecurityEvent>(e =>
                e.Kind == ApiTokenSecurityEventKind.AuthFailed)), Times.Once);
            _securityEvents.Verify(s => s.Record(It.Is<ApiTokenSecurityEvent>(e =>
                e.Source == "10.0.0.5:11111")), Times.Once);
        }

        [Fact]
        public async Task DeletedOwner_FailsClosed()
        {
            var credential = ValidCredential();
            SetupManagerAccepts(credential, BuildInfo() with { OwnerUserId = Guid.NewGuid() });
            _usersMock.Setup(u => u[It.IsAny<Guid>()]).Returns((User)null);

            var result = await AuthenticateAsync($"Bearer {credential}");

            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task Challenge_IsGeneric401WithoutRedirect()
        {
            var context = await ChallengeAsync();

            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
            Assert.Equal("Bearer", context.Response.Headers.WWWAuthenticate.ToString());
            Assert.True(Microsoft.Extensions.Primitives.StringValues.IsNullOrEmpty(context.Response.Headers.Location));
        }

        [Fact]
        public async Task Challenge_BodyIsTheUniformJsonErrorContract()
        {
            // #1353: the 401 is machine-readable too — same {error, message, details}
            // body as every other management-API error path.
            var context = await ChallengeAsync();

            context.Response.Body.Position = 0;
            using var body = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);

            var root = body.RootElement;

            Assert.Equal(HSMServer.Model.ManagementApi.ManagementApiErrors.UnauthorizedCode, root.GetProperty("error").GetString());
            Assert.False(string.IsNullOrEmpty(root.GetProperty("message").GetString()));
            Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("details").ValueKind);
        }

        [Fact]
        public async Task Success_MarksTokenUsed()
        {
            var credential = ValidCredential();
            SetupManagerAccepts(credential, BuildInfo());

            await AuthenticateAsync($"Bearer {credential}");

            _managerMock.Verify(m => m.MarkUsed(TokenId), Times.Once);
        }

        [Fact]
        public async Task Failure_DoesNotMarkTokenUsed()
        {
            var credential = ValidCredential();
            SetupManagerAccepts(credential, info: null);

            await AuthenticateAsync($"Bearer {credential}");

            _managerMock.Verify(m => m.MarkUsed(It.IsAny<string>()), Times.Never);
        }


        private delegate void TryAuthenticateOutCallback(string presented, out ApiTokenInfo info);
    }
}
