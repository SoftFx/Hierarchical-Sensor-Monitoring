using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Middleware;
using HSMServer.Model.ManagementApi;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HSMServer.Core.Tests.Middleware
{
    // The JSON half of the uniform error contract (#1353, epic #1347) at the PIPELINE
    // enforcement points, where no controller action ever runs: the shared response
    // writer (the only place the wire casing is observable), the area guard's 404, the
    // legacy bearer guard's 401, and the /api exception handler that keeps HTML off
    // every /api response. The controller-side enforcement is pinned in
    // Controllers/ManagementApiErrorContractTests; the authentication challenge in
    // HsmApiTokenHandlerTests.
    public class ApiJsonErrorContractTests
    {
        private const int SitePort = 44333;
        private const int SensorPort = 44330;

        private static readonly HsmListenerBindings Bindings = new(SitePort, SensorPort);


        private static HttpContext BuildContext(string path, int localPort = SitePort, Endpoint endpoint = null)
        {
            var context = new DefaultHttpContext();

            // DefaultHttpContext writes into Stream.Null — capture the body so the
            // contract assertions can parse what the middleware actually serialized.
            context.Response.Body = new MemoryStream();

            context.Request.Path = path;
            context.Features.Set<IHttpConnectionFeature>(new FixedPortConnectionFeature(localPort));
            if (endpoint is not null)
                context.SetEndpoint(endpoint);

            return context;
        }

        private static async Task<JsonDocument> BodyAsync(HttpContext context)
        {
            context.Response.Body.Position = 0;

            return await JsonDocument.ParseAsync(context.Response.Body);
        }


        [Fact]
        public async Task Writer_WireShape_IsCamelCaseJsonWithDetails()
        {
            var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

            await ManagementApiErrorResponses.WriteAsync(context, StatusCodes.Status403Forbidden,
                ManagementApiErrors.ForbiddenCode, "no grant",
                new Dictionary<string, string[]> { ["folderId"] = ["required"] });

            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
            Assert.StartsWith("application/json", context.Response.ContentType);

            var body = await BodyAsync(context);
            var root = body.RootElement;

            Assert.Equal(ManagementApiErrors.ForbiddenCode, root.GetProperty("error").GetString());
            Assert.Equal("no grant", root.GetProperty("message").GetString());
            Assert.Equal("required", root.GetProperty("details").GetProperty("folderId")[0].GetString());
        }

        [Fact]
        public async Task Writer_NullDetails_IsExplicitJsonNull()
        {
            var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

            await ManagementApiErrorResponses.WriteAsync(context, StatusCodes.Status404NotFound,
                ManagementApiErrors.NotFoundCode, ManagementApiErrors.NotFoundMessage);

            var root = (await BodyAsync(context)).RootElement;

            Assert.Equal(ManagementApiErrors.NotFoundCode, root.GetProperty("error").GetString());
            Assert.Equal(ManagementApiErrors.NotFoundMessage, root.GetProperty("message").GetString());
            // The three fields are ALWAYS present — an agent must never guess which
            // keys exist; absent details is an explicit null.
            Assert.Equal(JsonValueKind.Null, root.GetProperty("details").ValueKind);
        }

        [Fact]
        public async Task AreaGuard_Rejection_IsUniformJsonNotFound()
        {
            // Unmatched route in the area; the same body answers the wrong listener,
            // unmarked endpoints and anonymous endpoints.
            var context = BuildContext("/api/v1/no-such-resource", SitePort, endpoint: null);

            await new ManagementApiGuardMiddleware(_ => Task.CompletedTask, Bindings).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);

            var root = (await BodyAsync(context)).RootElement;

            // IDENTICAL to the controller's unknown-id 404 — the area's anti-
            // enumeration rule, now at the routing layer too.
            Assert.Equal(ManagementApiErrors.NotFoundCode, root.GetProperty("error").GetString());
            Assert.Equal(ManagementApiErrors.NotFoundMessage, root.GetProperty("message").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("details").ValueKind);
        }

        [Fact]
        public async Task SwaggerGate_OffSitePort_IsUniformJsonNotFound()
        {
            // The doc enumerates the SitePort-only management surface — serving it on
            // the sensor port would publish the map the area guard exists to hide
            // (review #1366). Both swagger path families are gated.
            foreach (var path in new[] { "/swagger/0.0.0/swagger.json", "/api/swagger/index.html" })
            {
                var context = BuildContext(path, SensorPort);

                await new SwaggerSitePortOnlyMiddleware(_ => Task.CompletedTask, Bindings).InvokeAsync(context);

                Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
                Assert.Equal(ManagementApiErrors.NotFoundCode, (await BodyAsync(context)).RootElement.GetProperty("error").GetString());
            }
        }

        [Fact]
        public async Task SwaggerGate_OnSitePort_PassesThrough()
        {
            var context = BuildContext("/api/swagger/index.html", SitePort);

            await new SwaggerSitePortOnlyMiddleware(_ => Task.CompletedTask, Bindings).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        [Fact]
        public async Task SwaggerGate_NonSwaggerPath_OnSensorPort_PassesThrough()
        {
            // Sensor-data traffic on the sensor port is none of this gate's business.
            var context = BuildContext("/api/sensors/int", SensorPort);

            await new SwaggerSitePortOnlyMiddleware(_ => Task.CompletedTask, Bindings).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        [Fact]
        public async Task LegacyGuard_HsmBearerOutsideArea_IsUniformJsonUnauthorized()
        {
            var credential = ApiTokenMaterial.FormatToken(new string('A', 22), new string('B', 43));
            var context = BuildContext("/Home/Index");
            context.Request.Headers.Authorization = $"Bearer {credential}";

            await new LegacyBearerGuardMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);

            var root = (await BodyAsync(context)).RootElement;

            Assert.Equal(ManagementApiErrors.UnauthorizedCode, root.GetProperty("error").GetString());
            Assert.False(string.IsNullOrEmpty(root.GetProperty("message").GetString()));
        }

        [Fact]
        public void BindingFailures_ManagementActions_GetTheUniformContract()
        {
            // [ApiController] automatic 400s (malformed JSON, wrong types) bypass the
            // action body, so they need their own route into the contract.
            var context = BindingContext(typeof(HSMServer.Controllers.AlertTemplatesApiController));
            context.ModelState.AddModelError("name", "The name field is required.");
            context.ModelState.AddModelError("$.policies[1]", "JSON syntax error.");

            var result = ManagementApiErrors.BindingFailureResponse(context);

            var body = Assert.IsType<ManagementApiErrorDto>(Assert.IsType<ObjectResult>(result).Value);

            Assert.Equal(400, Assert.IsType<ObjectResult>(result).StatusCode);
            Assert.Equal(ManagementApiErrors.ValidationFailedCode, body.Error);

            var details = Assert.IsType<Dictionary<string, string[]>>(body.Details);
            Assert.Contains("The name field is required.", details["name"]);
            Assert.Contains("JSON syntax error.", details["$.policies[1]"]);
        }

        [Fact]
        public void FromModelState_EmptyMessages_GetTheFallbackWording()
        {
            // A binder exception that is not a Format/Overflow/InputFormatter one
            // produces an EMPTY error message; a field key with "" is not actionable.
            var state = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
            state.AddModelError("root", string.Empty);

            var map = ManagementApiErrors.FromModelState(state);

            Assert.Equal("The input was not valid.", Assert.Single(map["root"]));
        }

        [Fact]
        public void BindingFailures_OtherApiControllers_DelegateToTheCapturedFrameworkDefault()
        {
            // The sensor-data API (SensorsController) is NOT part of the management
            // area: the wired factory must call through to the CAPTURED framework
            // default verbatim — its wire shape (problem+json, type, traceId) is
            // compatibility-sensitive, never a reimplementation (review #1366).
            IActionResult fromDefault = new OkResult();

            var factory = ManagementApiErrors.WrapBindingFailureFactory(_ => fromDefault);

            Assert.Same(fromDefault, factory(BindingContext(typeof(HSMServer.Controllers.SensorsController))));
        }

        [Fact]
        public void BindingFailures_ManagementActions_NeverCallTheFrameworkDefault()
        {
            IActionResult fromDefault = new OkResult();
            var context = BindingContext(typeof(HSMServer.Controllers.AlertTemplatesApiController));
            context.ModelState.AddModelError("name", "The name field is required.");

            var result = ManagementApiErrors.WrapBindingFailureFactory(_ => fromDefault)(context);

            Assert.NotSame(fromDefault, result);
            Assert.Equal(ManagementApiErrors.ValidationFailedCode,
                Assert.IsType<ManagementApiErrorDto>(Assert.IsType<ObjectResult>(result).Value).Error);
        }


        private static Microsoft.AspNetCore.Mvc.ActionContext BindingContext(Type controllerType) =>
            new(new DefaultHttpContext(), new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor
                {
                    ControllerTypeInfo = controllerType.GetTypeInfo(),
                },
                new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary());

        [Fact]
        public async Task ExceptionMiddleware_ApiPath_IsUniformJson500WithTraceId()
        {
            var context = BuildContext("/api/v1/alertTemplates");

            static Task Throw(HttpContext _) => throw new InvalidOperationException("boom");

            await new ApiExceptionJsonMiddleware(Throw).InvokeAsync(context);

            Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

            var root = (await BodyAsync(context)).RootElement;

            Assert.Equal(ManagementApiErrors.InternalErrorCode, root.GetProperty("error").GetString());
            Assert.False(string.IsNullOrEmpty(root.GetProperty("message").GetString()));
            // The only field that may vary per incident: quoting the trace id in a bug
            // report must locate the server-side log record.
            Assert.Equal(context.TraceIdentifier, root.GetProperty("details").GetProperty("traceId").GetString());
            // The exception itself never reaches the wire.
            Assert.DoesNotContain("boom", root.GetRawText());
        }

        [Fact]
        public static async Task ExceptionMiddleware_NonApiPath_RethrowsForTheRazorHandler()
        {
            var context = BuildContext("/Home/Index");

            static Task Throw(HttpContext _) => throw new InvalidOperationException("boom");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ApiExceptionJsonMiddleware(Throw).InvokeAsync(context));
        }

        [Fact]
        public static async Task ExceptionMiddleware_StartedResponse_Rethrows()
        {
            // Nothing can rewrite a response already on the wire — propagate and let
            // the connection die, same as the global exception handler.
            var context = new DefaultHttpContext();
            context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
            context.Request.Path = "/api/v1/alertTemplates";

            static Task Throw(HttpContext _) => throw new InvalidOperationException("boom");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ApiExceptionJsonMiddleware(Throw).InvokeAsync(context));
        }


        private sealed class FixedPortConnectionFeature : IHttpConnectionFeature
        {
            public FixedPortConnectionFeature(int localPort) => LocalPort = localPort;

            public IPAddress RemoteIpAddress { get; set; }

            public int RemotePort { get; set; }

            public IPAddress LocalIpAddress { get; set; }

            public int LocalPort { get; set; }

            public string ConnectionId { get; set; }
        }

        // The only way to make a DefaultHttpContext response look "already started"
        // without going through the server.
        private sealed class StartedResponseFeature : IHttpResponseFeature
        {
            public Stream Body { get; set; } = Stream.Null;

            public int StatusCode { get; set; }

            public string ReasonPhrase { get; set; }

            public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

            public bool HasStarted => true;

            public void OnCompleted(Func<object, Task> callback, object state) { }

            public void OnStarting(Func<object, Task> callback, object state) { }
        }
    }
}
