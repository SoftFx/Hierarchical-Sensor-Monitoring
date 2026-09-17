using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.BackgroundServices;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Middleware;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Middleware
{
    // The measurement half of the API-token usage monitoring (#1402):
    // attribution of /api/v1 + /mcp requests to the authenticating token
    // (owner login + EntityId, never the TokenId or the name), the REST/MCP
    // split, the 401-only auth-failure aggregate, and the never-break rule
    // for the measured request. The sensors surface is mocked via
    // IApiTokenUsageMonitor — the registry itself lives inside the booted
    // collector and is exercised by the E2E surface.
    public class ApiTokenUsageMiddlewareTests
    {
        private const string TokenId = "AAAAAAAAAAAAAAAAAAAAAA";

        private delegate void TryGetEntityIdCallback(string tokenId, out Guid entityId);

        private readonly Mock<IApiTokenUsageMonitor> _monitor = new();
        private readonly Mock<IApiTokenManager> _tokens = new();
        private readonly Mock<IUserManager> _users = new();

        private readonly Guid _ownerId = Guid.NewGuid();
        private readonly Guid _entityId = Guid.NewGuid();
        private readonly string _login = "ops.user";


        public ApiTokenUsageMiddlewareTests()
        {
            _monitor.Setup(m => m.Enabled).Returns(true);

            _tokens.Setup(t => t.TryGetEntityId(TokenId, out It.Ref<Guid>.IsAny))
                .Callback(new TryGetEntityIdCallback((string _, out Guid id) => id = _entityId))
                .Returns(true);

            _users.Setup(u => u[_ownerId]).Returns(new User(_login));
        }


        private ApiTokenUsageMiddleware CreateMiddleware(RequestDelegate next) =>
            new(next, _monitor.Object, _tokens.Object, _users.Object);


        private static DefaultHttpContext Context(string path, ClaimsPrincipal user = null, int statusCode = StatusCodes.Status200OK) =>
            new()
            {
                Request = { Path = path },
                User = user ?? new ClaimsPrincipal(new ClaimsIdentity()),
                Response = { StatusCode = statusCode },
            };

        private ClaimsPrincipal TokenPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, _ownerId.ToString()),
                new Claim(HsmApiTokenClaims.TokenId, TokenId),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        // VerifyNoOtherCalls counts the Enabled getter as an unverified
        // invocation — account for it, then assert nothing else was called.
        private void VerifySilent()
        {
            _monitor.Verify(m => m.Enabled, Times.AtMostOnce);
            _monitor.VerifyNoOtherCalls();
        }


        [Fact]
        public async Task TokenRequest_OnApiV1_AttributesToTheToken()
        {
            var context = Context("/api/v1/products", TokenPrincipal());

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            _monitor.Verify(m => m.AddRestRequest(_login, _entityId, It.Is<double>(ms => ms >= 0)), Times.Once);
            _monitor.Verify(m => m.AddMcpRequest(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<double>()), Times.Never);
            _monitor.Verify(m => m.AddAuthenticationFailure(), Times.Never);
        }


        [Fact]
        public async Task TokenIdentity_MaterializingDuringNext_AttributesToTheToken()
        {
            // The REAL pipeline shape (#1402 review blocker): HsmApiToken is
            // not the default scheme, so the request starts with whatever
            // UseAuthentication produced (nothing, for a bearer request) and
            // the token principal materializes INSIDE next — Authorization-
            // Middleware authenticates the policy's schemes and replaces
            // context.User. Resolving at observation time must see it.
            var context = Context("/api/v1/products");

            Task ReplaceUser(HttpContext _)
            {
                context.User = TokenPrincipal();
                return Task.CompletedTask;
            }

            await CreateMiddleware(ReplaceUser).InvokeAsync(context);

            _monitor.Verify(m => m.AddRestRequest(_login, _entityId, It.IsAny<double>()), Times.Once);
        }


        [Fact]
        public async Task TokenRequest_OnMcp_UsesTheMcpChannel()
        {
            var context = Context("/mcp", TokenPrincipal());

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            _monitor.Verify(m => m.AddMcpRequest(_login, _entityId, It.IsAny<double>()), Times.Once);
            _monitor.Verify(m => m.AddRestRequest(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<double>()), Times.Never);
        }


        [Fact]
        public async Task McpPath_BeyondTheEndpoint_IsNotMeasured()
        {
            // /mcp is measured as the exact ENDPOINT, not the prefix — the
            // same semantics as the bearer guard's exemption (#1392):
            // anything else under /mcp is not token traffic.
            var context = Context("/mcp/other", TokenPrincipal());
            var reached = false;

            await CreateMiddleware(_ => { reached = true; return Task.CompletedTask; }).InvokeAsync(context);

            Assert.True(reached);
            VerifySilent();
        }


        [Fact]
        public async Task TokenRequest_PathIsCaseInsensitiveForTheChannel()
        {
            // Endpoint routing matches segments case-insensitively; the REST/MCP
            // split must not disagree with what actually executed.
            var context = Context("/MCP", TokenPrincipal());

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            _monitor.Verify(m => m.AddMcpRequest(_login, _entityId, It.IsAny<double>()), Times.Once);
        }


        [Fact]
        public async Task TokenRequest_EveryStatusCodeCounts()
        {
            // A 403/404/409 answer is still work the token asked for — duration
            // and rate must carry it (#1402 design).
            var context = Context("/api/v1/sensors/00000000-0000-0000-0000-000000000000", TokenPrincipal(),
                StatusCodes.Status404NotFound);

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            _monitor.Verify(m => m.AddRestRequest(_login, _entityId, It.IsAny<double>()), Times.Once);
        }


        [Fact]
        public async Task NoToken_401_CountsAuthenticationFailureOnly()
        {
            var context = Context("/api/v1/products", statusCode: StatusCodes.Status401Unauthorized);

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            _monitor.Verify(m => m.AddAuthenticationFailure(), Times.Once);
            _monitor.Verify(m => m.AddRestRequest(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<double>()), Times.Never);
        }


        [Fact]
        public async Task TokenIdentity_With401_AttributesUsage_NoAuthFailureTick()
        {
            // A resolved token identity with a 401 answer (a read-forbidden
            // path can produce it) is still attributed USAGE — the request
            // was token work — and must NOT tick the aggregate failure
            // counter: that counter is for tokenLESS failures. Observe
            // guards this with `tokenIdentity is null`; nothing else holds
            // the guard in place.
            var context = Context("/api/v1/products", TokenPrincipal(), StatusCodes.Status401Unauthorized);

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            _monitor.Verify(m => m.AddRestRequest(_login, _entityId, It.IsAny<double>()), Times.Once);
            _monitor.Verify(m => m.AddAuthenticationFailure(), Times.Never);
        }


        [Fact]
        public async Task CookiePrincipal_200_NothingCounted()
        {
            // The reserved /api/v1/api-tokens family is excluded from
            // measurement BY PATH (#1403 r2): whatever principal it carries,
            // it is cookie traffic, not token usage. (On a NON-reserved
            // endpoint a cookie principal cannot reach a 200 — authorization
            // replaces it with the failed token-scheme authentication and
            // answers 401, which the aggregate-counter test covers.)
            var cookieUser = new ClaimsPrincipal(new ClaimsIdentity("Cookies"));
            var context = Context("/api/v1/api-tokens", cookieUser);

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            VerifySilent();
        }


        [Fact]
        public async Task CookieFamily_401_NotATokenAuthFailure()
        {
            // An expired browser session on the reserved cookie-only family
            // answers 401 (MyCookieAuthenticationEvents never redirects under
            // /api/v1) — that is a COOKIE failure, not a token-credential
            // one, and must not tick the aggregate counter (#1403 review r2).
            var context = Context("/api/v1/api-tokens", statusCode: StatusCodes.Status401Unauthorized);

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            VerifySilent();
        }


        [Fact]
        public async Task PurgedMidRequest_NoAttributionNoFailure()
        {
            // The principal authenticated, but the token record was purged (revocation
            // (purge race): neither a usage tick nor an auth failure —
            // the request itself answered 200 through the real pipeline.
            _tokens.Setup(t => t.TryGetEntityId(TokenId, out It.Ref<Guid>.IsAny)).Returns(false);

            var context = Context("/api/v1/products", TokenPrincipal());

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            VerifySilent();
        }


        [Fact]
        public async Task UnmeasuredPath_PassesThroughUncounted()
        {
            var context = Context("/Home/Index", TokenPrincipal());
            var reached = false;

            await CreateMiddleware(_ => { reached = true; return Task.CompletedTask; }).InvokeAsync(context);

            Assert.True(reached);
            VerifySilent();
        }


        [Fact]
        public async Task SensorThrow_NeverBreaksTheRequest()
        {
            // Metrics are best-effort by contract: a failure inside the
            // observation must not replace the outcome of a finished request.
            _monitor.Setup(m => m.AddRestRequest(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<double>()))
                .Throws(new InvalidOperationException("collector is down"));

            var context = Context("/api/v1/products", TokenPrincipal());
            var answered = false;

            await CreateMiddleware(_ => { answered = true; return Task.CompletedTask; }).InvokeAsync(context);

            Assert.True(answered);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }


        [Fact]
        public async Task NextThrows_ExceptionPropagatesAndStillAttributed()
        {
            // The other half of the finally contract: when the measured
            // pipeline itself throws, the observation still runs AND the
            // ORIGINAL exception leaves the middleware unchanged — a future
            // refactor that moves Observe out of the finally (or wraps it in
            // a catch) must fail here, not in production (#1403 review).
            var context = Context("/api/v1/products", TokenPrincipal());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateMiddleware(_ => throw new InvalidOperationException("handler failed")).InvokeAsync(context));

            _monitor.Verify(m => m.AddRestRequest(_login, _entityId, It.IsAny<double>()), Times.Once);
        }


        [Theory]
        [InlineData("a/b", "a_b")]          // separators would split the segment
        [InlineData("a\\b", "a_b")]
        [InlineData(" ops.user ", "ops.user")] // trimmed, not part of the identity
        [InlineData("ops.user", "ops.user")]
        [InlineData("/", "_")]              // never an empty path segment
        [InlineData(null, "_")]             // a missing name keeps the contract total
        [InlineData("   ", "_")]
        public void SanitizeLogin_KeepsTheSegmentWhole(string login, string expected) =>
            Assert.Equal(expected, HSMServer.BackgroundServices.ApiTokenUsageNode.SanitizeLogin(login));


        [Fact]
        public async Task MonitoringDisabled_PassesThroughUncounted()
        {
            // With self-monitoring off the collector never publishes and the
            // sweep never runs — measurement would only burn lookups and
            // register sensors into a dead pipeline (#1403 review r3).
            _monitor.Setup(m => m.Enabled).Returns(false);

            var context = Context("/api/v1/products", TokenPrincipal());
            var reached = false;

            await CreateMiddleware(_ => { reached = true; return Task.CompletedTask; }).InvokeAsync(context);

            Assert.True(reached);
            VerifySilent();
        }
    }
}
