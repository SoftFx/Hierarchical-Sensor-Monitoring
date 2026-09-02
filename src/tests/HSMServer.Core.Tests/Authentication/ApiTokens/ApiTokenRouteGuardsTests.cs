using System;
using System.Net;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Middleware;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // Fail-closed route guards of the management area (initiative step 3):
    //  - LegacyBearerGuardMiddleware: an hsm_pat_ bearer outside /api/v1 gets a generic
    //    non-redirecting 401 before MVC, with no token lookup — it can never reach a
    //    BaseController cast or an [Authorize]-only legacy action.
    //  - ManagementApiGuardMiddleware: /api/v1 is allow-listed per endpoint (marker +
    //    policy, never anonymous) and only on the SitePort listener; anything else in the
    //    area is 404, so a newly added route without the metadata is unreachable by default.
    public class ApiTokenRouteGuardsTests
    {
        private const int SitePort = 44333;
        private const int SensorPort = 44330;

        private static readonly HsmListenerBindings Bindings = new(SitePort, SensorPort);


        private static HttpContext BuildContext(string path, int localPort,
            Endpoint endpoint = null, string authorization = null)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            context.Features.Set<IHttpConnectionFeature>(new FixedPortConnectionFeature(localPort));
            if (endpoint is not null)
                context.SetEndpoint(endpoint);
            if (authorization is not null)
                context.Request.Headers.Authorization = authorization;

            return context;
        }

        // A minimal action-less endpoint: the guards read metadata and never invoke it.
        private static Endpoint Endpoint(params object[] metadata) =>
            new(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test");

        private static Task NextThatMarks(HttpContext context)
        {
            context.Items["reached"] = true;
            return Task.CompletedTask;
        }


        [Fact]
        public async Task LegacyGuard_HsmBearerOutsideManagementArea_GetsPlain401AndShortCircuits()
        {
            var credential = ApiTokenMaterial.FormatToken(new string('A', 22), new string('B', 43));
            var context = BuildContext("/Home/Index", SitePort, authorization: $"Bearer {credential}");

            await new LegacyBearerGuardMiddleware(NextThatMarks).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
            Assert.True(Microsoft.Extensions.Primitives.StringValues.IsNullOrEmpty(context.Response.Headers.Location));
            Assert.False(context.Items.ContainsKey("reached"));
        }

        [Theory]
        [InlineData(null)]                                    // no credential at all
        [InlineData("Bearer foreign_token")]                  // a different bearer format
        [InlineData("Basic dXNlcjpwYXNz")]                    // another scheme
        public async Task LegacyGuard_OtherCredentials_PassThrough(string authorization)
        {
            var context = BuildContext("/Home/Index", SitePort, authorization: authorization);

            await new LegacyBearerGuardMiddleware(NextThatMarks).InvokeAsync(context);

            Assert.True(context.Items.ContainsKey("reached"));
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        [Fact]
        public async Task LegacyGuard_HsmBearerInsideManagementArea_PassesToAreaGuard()
        {
            var credential = ApiTokenMaterial.FormatToken(new string('A', 22), new string('B', 43));
            var context = BuildContext("/api/v1/products", SitePort, authorization: $"Bearer {credential}");

            await new LegacyBearerGuardMiddleware(NextThatMarks).InvokeAsync(context);

            Assert.True(context.Items.ContainsKey("reached"));
        }

        [Fact]
        public async Task LegacyGuard_DuplicatedAuthorizationValues_StillCatchHsmBearer()
        {
            // StringValues.ToString() joins duplicated headers with ", ": parsing the
            // joined string would read the scheme of the FIRST value and miss the bearer.
            // The guard inspects each header value on its own.
            var credential = ApiTokenMaterial.FormatToken(new string('A', 22), new string('B', 43));
            var context = BuildContext("/Home/Index", SitePort);
            context.Request.Headers.Append("Authorization", "Basic dXNlcjpwYXNz");
            context.Request.Headers.Append("Authorization", $"Bearer {credential}");

            await new LegacyBearerGuardMiddleware(NextThatMarks).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
            Assert.False(context.Items.ContainsKey("reached"));
        }

        [Fact]
        public async Task AreaGuard_ManagementEndpointOnSitePort_Passes()
        {
            var endpoint = Endpoint(new ManagementApiAttribute(),
                new AuthorizeAttribute(HsmApiTokenDefaults.ManagementPolicy));
            var context = BuildContext("/api/v1/products", SitePort, endpoint);

            await new ManagementApiGuardMiddleware(NextThatMarks, Bindings).InvokeAsync(context);

            Assert.True(context.Items.ContainsKey("reached"));
        }

        [Fact]
        public async Task AreaGuard_ManagementEndpointOnSensorPort_Is404()
        {
            var endpoint = Endpoint(new ManagementApiAttribute(),
                new AuthorizeAttribute(HsmApiTokenDefaults.ManagementPolicy));
            var context = BuildContext("/api/v1/products", SensorPort, endpoint);

            await new ManagementApiGuardMiddleware(NextThatMarks, Bindings).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.False(context.Items.ContainsKey("reached"));
        }

        [Fact]
        public async Task AreaGuard_EndpointWithoutManagementMarker_Is404()
        {
            // A route added under /api/v1 without the area metadata is unavailable by
            // default, even on the SitePort listener.
            var endpoint = Endpoint(new AuthorizeAttribute(HsmApiTokenDefaults.ManagementPolicy));
            var context = BuildContext("/api/v1/products", SitePort, endpoint);

            await new ManagementApiGuardMiddleware(NextThatMarks, Bindings).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        [Fact]
        public async Task AreaGuard_AnonymousManagementEndpoint_Is404()
        {
            var endpoint = Endpoint(new ManagementApiAttribute(), new AllowAnonymousAttribute());
            var context = BuildContext("/api/v1/products", SitePort, endpoint);

            await new ManagementApiGuardMiddleware(NextThatMarks, Bindings).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        [Fact]
        public async Task AreaGuard_MarkerWithoutManagementPolicy_Is404()
        {
            // Marker but bare [Authorize] (cookie default policy): still unavailable —
            // ordinary /api/v1 routes must authenticate through the HsmApiToken policy.
            var endpoint = Endpoint(new ManagementApiAttribute(), new AuthorizeAttribute());
            var context = BuildContext("/api/v1/products", SitePort, endpoint);

            await new ManagementApiGuardMiddleware(NextThatMarks, Bindings).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        [Fact]
        public async Task AreaGuard_NoEndpointInManagementArea_Is404()
        {
            var context = BuildContext("/api/v1/unknown", SitePort, endpoint: null);

            await new ManagementApiGuardMiddleware(NextThatMarks, Bindings).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        [Fact]
        public async Task AreaGuard_ReservedCookieOnlyTokenRoutes_AcceptCookiePolicy()
        {
            // /api/v1/api-tokens is the sole v1 cookie-only family (step 4): its endpoints
            // use the default cookie authorization, not the token policy, and must pass.
            var endpoint = Endpoint(new ManagementApiAttribute(), new AuthorizeAttribute());
            var context = BuildContext("/api/v1/api-tokens", SitePort, endpoint);

            await new ManagementApiGuardMiddleware(NextThatMarks, Bindings).InvokeAsync(context);

            Assert.True(context.Items.ContainsKey("reached"));
        }

        [Fact]
        public async Task AreaGuard_ReservedCookieOnlyRouteWithoutAuthorize_Is404()
        {
            // Absence of [Authorize] is not [AllowAnonymous] — but with no fallback policy
            // it is just as anonymous. The reserved family must carry a cookie [Authorize]
            // to be reachable: these are the routes that mint and revoke tokens.
            var endpoint = Endpoint(new ManagementApiAttribute());
            var context = BuildContext("/api/v1/api-tokens", SitePort, endpoint);

            await new ManagementApiGuardMiddleware(NextThatMarks, Bindings).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.False(context.Items.ContainsKey("reached"));
        }

        [Fact]
        public async Task AreaGuard_ReservedCookieOnlyRouteWithManagementPolicy_Is404()
        {
            // The reserved family is cookie-only: the management (token) policy does not
            // make an endpoint under /api/v1/api-tokens reachable.
            var endpoint = Endpoint(new ManagementApiAttribute(),
                new AuthorizeAttribute(HsmApiTokenDefaults.ManagementPolicy));
            var context = BuildContext("/api/v1/api-tokens", SitePort, endpoint);

            await new ManagementApiGuardMiddleware(NextThatMarks, Bindings).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        [Fact]
        public async Task AreaGuard_ReservedCookieOnlyRouteWithSchemeBearingAuthorize_Is404()
        {
            // [Authorize(AuthenticationSchemes = HsmApiToken)] has an empty Policy, but
            // AuthorizationPolicy.Combine unions its schemes into the cookie-pinned
            // DefaultPolicy — a token principal would enter the cookie-only family. The
            // guard admits only scheme-less bare [Authorize].
            var endpoint = Endpoint(new ManagementApiAttribute(),
                new AuthorizeAttribute { AuthenticationSchemes = HsmApiTokenDefaults.AuthenticationScheme });
            var context = BuildContext("/api/v1/api-tokens", SitePort, endpoint);

            await new ManagementApiGuardMiddleware(NextThatMarks, Bindings).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        [Fact]
        public async Task AreaGuard_ReservedCookieOnlyTokenRoutesOnSensorPort_Are404()
        {
            var endpoint = Endpoint(new ManagementApiAttribute(), new AuthorizeAttribute());
            var context = BuildContext("/api/v1/api-tokens", SensorPort, endpoint);

            await new ManagementApiGuardMiddleware(NextThatMarks, Bindings).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        [Fact]
        public async Task AreaGuard_PathOutsideManagementArea_PassesThrough()
        {
            var endpoint = Endpoint(); // even a legacy anonymous endpoint
            var context = BuildContext("/api/sensors", SensorPort, endpoint);

            await new ManagementApiGuardMiddleware(NextThatMarks, Bindings).InvokeAsync(context);

            Assert.True(context.Items.ContainsKey("reached"));
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
