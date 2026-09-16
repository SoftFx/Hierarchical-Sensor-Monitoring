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

        private readonly Mock<IApiTokenUsageMonitor> _monitor = new();
        private readonly Mock<IApiTokenManager> _tokens = new();
        private readonly Mock<IUserManager> _users = new();

        private readonly Guid _ownerId = Guid.NewGuid();
        private readonly Guid _entityId = Guid.NewGuid();
        private readonly string _login = "ops.user";


        public ApiTokenUsageMiddlewareTests()
        {
            _tokens.Setup(t => t.GetToken(TokenId))
                .Returns(new ApiTokenInfo { EntityId = _entityId, OwnerUserId = _ownerId });

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


        [Fact]
        public async Task TokenRequest_OnApiV1_AttributesToTheToken()
        {
            var context = Context("/api/v1/products", TokenPrincipal());

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            _monitor.Verify(m => m.AddRestRequest(_login, _entityId.ToString("D"), It.Is<double>(ms => ms >= 0)), Times.Once);
            _monitor.Verify(m => m.AddMcpRequest(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>()), Times.Never);
            _monitor.Verify(m => m.AddAuthenticationFailure(), Times.Never);
        }


        [Fact]
        public async Task TokenRequest_OnMcp_UsesTheMcpChannel()
        {
            var context = Context("/mcp", TokenPrincipal());

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            _monitor.Verify(m => m.AddMcpRequest(_login, _entityId.ToString("D"), It.IsAny<double>()), Times.Once);
            _monitor.Verify(m => m.AddRestRequest(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>()), Times.Never);
        }


        [Fact]
        public async Task TokenRequest_PathIsCaseInsensitiveForTheChannel()
        {
            // Endpoint routing matches segments case-insensitively; the REST/MCP
            // split must not disagree with what actually executed.
            var context = Context("/MCP", TokenPrincipal());

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            _monitor.Verify(m => m.AddMcpRequest(_login, _entityId.ToString("D"), It.IsAny<double>()), Times.Once);
        }


        [Fact]
        public async Task TokenRequest_EveryStatusCodeCounts()
        {
            // A 403/404/409 answer is still work the token asked for — duration
            // and rate must carry it (#1402 design).
            var context = Context("/api/v1/sensors/00000000-0000-0000-0000-000000000000", TokenPrincipal(),
                StatusCodes.Status404NotFound);

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            _monitor.Verify(m => m.AddRestRequest(_login, _entityId.ToString("D"), It.IsAny<double>()), Times.Once);
        }


        [Fact]
        public async Task NoToken_401_CountsAuthenticationFailureOnly()
        {
            var context = Context("/api/v1/products", statusCode: StatusCodes.Status401Unauthorized);

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            _monitor.Verify(m => m.AddAuthenticationFailure(), Times.Once);
            _monitor.Verify(m => m.AddRestRequest(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>()), Times.Never);
        }


        [Fact]
        public async Task CookiePrincipal_200_NothingCounted()
        {
            // The /api/v1/api-tokens family authenticates through cookies —
            // not token usage, not a token auth failure.
            var cookieUser = new ClaimsPrincipal(new ClaimsIdentity("Cookies"));
            var context = Context("/api/v1/api-tokens", cookieUser);

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            _monitor.VerifyNoOtherCalls();
        }


        [Fact]
        public async Task RevokedMidRequest_NoAttributionNoFailure()
        {
            // The principal authenticated, but the token record is already gone
            // (revocation race): neither a usage tick nor an auth failure —
            // the request itself answered 200 through the real pipeline.
            _tokens.Setup(t => t.GetToken(TokenId)).Returns((ApiTokenInfo)null);

            var context = Context("/api/v1/products", TokenPrincipal());

            await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            _monitor.VerifyNoOtherCalls();
        }


        [Fact]
        public async Task UnmeasuredPath_PassesThroughUncounted()
        {
            var context = Context("/Home/Index", TokenPrincipal());
            var reached = false;

            await CreateMiddleware(_ => { reached = true; return Task.CompletedTask; }).InvokeAsync(context);

            Assert.True(reached);
            _monitor.VerifyNoOtherCalls();
        }


        [Fact]
        public async Task SensorThrow_NeverBreaksTheRequest()
        {
            // Metrics are best-effort by contract: a failure inside the
            // observation must not replace the outcome of a finished request.
            _monitor.Setup(m => m.AddRestRequest(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>()))
                .Throws(new InvalidOperationException("collector is down"));

            var context = Context("/api/v1/products", TokenPrincipal());
            var answered = false;

            await CreateMiddleware(_ => { answered = true; return Task.CompletedTask; }).InvokeAsync(context);

            Assert.True(answered);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }


        [Theory]
        [InlineData("a/b", "a_b")]          // separators would split the segment
        [InlineData("a\\b", "a_b")]
        [InlineData(" ops.user ", "ops.user")] // trimmed, not part of the identity
        [InlineData("ops.user", "ops.user")]
        public void SanitizeLogin_KeepsTheSegmentWhole(string login, string expected) =>
            Assert.Equal(expected, ApiTokenUsageMiddleware.SanitizeLogin(login));
    }
}
