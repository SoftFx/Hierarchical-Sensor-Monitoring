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


        private async Task<AuthenticateResult> AuthenticateAsync(string authorizationHeader)
        {
            var (provider, context) = Build(authorizationHeader);

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
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, HsmApiTokenHandler>(Scheme, _ => { });

            var provider = services.BuildServiceProvider();

            var context = new DefaultHttpContext { RequestServices = provider.CreateScope().ServiceProvider };
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
